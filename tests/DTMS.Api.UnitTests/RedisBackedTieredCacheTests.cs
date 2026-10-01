using System.Net;
using DTMS.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace DTMS.Api.UnitTests;

// Fail-open contract: Redis trouble is a miss on read and best-effort on
// write, so read-through callers (credentials, system clients, callback
// subscriptions) fall back to the database instead of failing. Invalidation
// is the exception — it still throws, and is replayed on reconnect.
public class RedisBackedTieredCacheTests
{
    private sealed record Payload(string Value);

    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly ISubscriber _sub = Substitute.For<ISubscriber>();

    public RedisBackedTieredCacheTests()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);
        _redis.GetSubscriber(Arg.Any<object?>()).Returns(_sub);
        _redis.IsConnected.Returns(true);
    }

    private RedisBackedTieredCache Cache() =>
        new(_redis, new MemoryCache(new MemoryCacheOptions()), new PodIdentity(),
            NullLogger<RedisBackedTieredCache>.Instance);

    [Fact]
    public async Task Get_WhenDisconnected_MissesWithoutTouchingRedis()
    {
        _redis.IsConnected.Returns(false);

        var result = await Cache().GetAsync<Payload>("k");

        result.Should().BeNull();
        await _db.DidNotReceiveWithAnyArgs().StringGetAsync(default(RedisKey));
    }

    [Fact]
    public async Task Get_WhenRedisTimesOut_ReturnsMissInsteadOfThrowing()
    {
        _db.StringGetAsync(default(RedisKey))
            .ThrowsAsyncForAnyArgs(new RedisTimeoutException("timeout", CommandStatus.Sent));

        var act = () => Cache().GetAsync<Payload>("k");

        (await act.Should().NotThrowAsync()).Subject.Should().BeNull();
    }

    [Fact]
    public async Task Set_DoesNotWaitForRedis_AndTheValueIsServedFromL1()
    {
        // A Redis write that never completes — the caller must not wait on it.
        var hang = new TaskCompletionSource<bool>();
        _db.StringSetAsync(default(RedisKey), default(RedisValue), default(TimeSpan?))
            .ReturnsForAnyArgs(hang.Task);
        var cache = Cache();

        var set = cache.SetAsync("k", new Payload("v"), TimeSpan.FromMinutes(5));

        set.IsCompleted.Should().BeTrue();
        (await cache.GetAsync<Payload>("k"))!.Value.Should().Be("v");
        await _db.DidNotReceiveWithAnyArgs().StringGetAsync(default(RedisKey));
    }

    [Fact]
    public async Task Set_WhenRedisWriteFails_DoesNotThrowOrBroadcast()
    {
        _db.StringSetAsync(default(RedisKey), default(RedisValue), default(TimeSpan?))
            .ThrowsAsyncForAnyArgs(new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

        await Cache().SetAsync("k", new Payload("v"), TimeSpan.FromMinutes(5));
        await Task.Delay(50); // let the background write run

        await _sub.DidNotReceiveWithAnyArgs().PublishAsync(default, default);
    }

    [Fact]
    public async Task Invalidate_WhenRedisFails_StillThrows_ClearsL1_AndReplaysOnReconnect()
    {
        var cache = Cache();
        await cache.SetAsync("k", new Payload("v"), TimeSpan.FromMinutes(5));
        _db.KeyDeleteAsync(default(RedisKey))
            .ThrowsAsyncForAnyArgs(new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

        var act = () => cache.InvalidateAsync("k");

        await act.Should().ThrowAsync<RedisConnectionException>();
        _redis.IsConnected.Returns(false);
        (await cache.GetAsync<Payload>("k")).Should().BeNull("L1 is cleared even though Redis failed");

        // Redis comes back: the queued key is deleted again.
        _db.KeyDeleteAsync(default(RedisKey)).ReturnsForAnyArgs(true);
        _db.ClearReceivedCalls();
        _redis.ConnectionRestored += Raise.EventWith(_redis, new ConnectionFailedEventArgs(
            _redis, new DnsEndPoint("redis", 6379), ConnectionType.Interactive,
            ConnectionFailureType.None, null!, "redis:6379"));
        await Task.Delay(50);

        await _db.Received(1).KeyDeleteAsync((RedisKey)"k", Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task HappyPath_SetWritesRedisThenBroadcasts_AndGetReadsRedisOnL1Miss()
    {
        _db.StringSetAsync(default(RedisKey), default(RedisValue), default(TimeSpan?))
            .ReturnsForAnyArgs(true);
        _db.StringGetAsync(default(RedisKey)).ReturnsForAnyArgs((RedisValue)"""{"value":"from-redis"}""");

        await Cache().SetAsync("k", new Payload("v"), TimeSpan.FromMinutes(5));
        await Task.Delay(50);
        var fromRedis = await Cache().GetAsync<Payload>("other"); // fresh L1 → goes to Redis

        await _db.ReceivedWithAnyArgs(1).StringSetAsync(default(RedisKey), default(RedisValue), default(TimeSpan?));
        await _sub.ReceivedWithAnyArgs(1).PublishAsync(default, default);
        fromRedis!.Value.Should().Be("from-redis");
    }
}
