using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DTMS.Infrastructure.Caching;

/// <summary>
/// Listens on the Redis invalidation channel and drops the named key from
/// this pod's L1 cache. Pairs with <see cref="RedisBackedTieredCache.InvalidateAsync"/>
/// — the originating pod already dropped its own L1 + L2 entry; this
/// service catches the broadcast on every other pod so divergence is
/// bounded by one Redis round-trip (typically &lt;10ms LAN). Local echo
/// is harmless because <see cref="IMemoryCache.Remove"/> on a missing
/// key is a no-op.
/// </summary>
public sealed class CacheInvalidationSubscriber : IHostedService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMemoryCache _local;
    private readonly ILogger<CacheInvalidationSubscriber> _log;
    private ChannelMessageQueue? _queue;

    public CacheInvalidationSubscriber(
        IConnectionMultiplexer redis,
        IMemoryCache local,
        ILogger<CacheInvalidationSubscriber> log)
    {
        _redis = redis;
        _local = local;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var sub = _redis.GetSubscriber();
        _queue = await sub.SubscribeAsync(
            RedisChannel.Literal(RedisBackedTieredCache.InvalidationChannel));

        _queue.OnMessage(msg =>
        {
            string? key = msg.Message;
            if (string.IsNullOrEmpty(key))
                return;

            try
            {
                _local.Remove(key);
            }
            catch (Exception ex)
            {
                // Subscriber callbacks must never throw — StackExchange.Redis
                // will tear down the subscription otherwise.
                _log.LogWarning(ex, "Failed to evict L1 entry for key {Key}", key);
            }
        });

        _log.LogInformation(
            "Subscribed to cache invalidation channel {Channel}",
            RedisBackedTieredCache.InvalidationChannel);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_queue is not null)
            await _queue.UnsubscribeAsync();
    }
}
