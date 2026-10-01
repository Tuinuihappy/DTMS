using System.Collections.Concurrent;
using System.Text.Json;
using DTMS.SharedKernel.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DTMS.Infrastructure.Caching;

public sealed class RedisBackedTieredCache : ITieredCache
{
    public const string InvalidationChannel = "dtms:cache:invalidate";

    private static readonly TimeSpan L1Ttl = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly IMemoryCache _local;
    private readonly PodIdentity _pod;
    private readonly ILogger<RedisBackedTieredCache> _log;

    // Keys whose invalidation could not reach Redis. Redis persists to disk,
    // so the stale L2 value would otherwise outlive the outage by up to its
    // TTL; they are deleted + re-broadcast as soon as the connection returns.
    private readonly ConcurrentDictionary<string, byte> _pendingInvalidations = new();

    public RedisBackedTieredCache(
        IConnectionMultiplexer redis,
        IMemoryCache local,
        PodIdentity pod,
        ILogger<RedisBackedTieredCache> log)
    {
        _redis = redis;
        _local = local;
        _pod = pod;
        _log = log;

        // One line per state change instead of one per cache miss. Only the
        // interactive connection matters here; pub/sub has its own.
        _redis.ConnectionFailed += (_, e) =>
        {
            if (e.ConnectionType == ConnectionType.Interactive)
                _log.LogWarning("Redis connection failed ({FailureType}) — tiered cache now reads through to its stores", e.FailureType);
        };
        _redis.ConnectionRestored += (_, e) =>
        {
            if (e.ConnectionType != ConnectionType.Interactive) return;
            _log.LogInformation("Redis connection restored — tiered cache back on L2");
            _ = ReplayPendingInvalidationsAsync();
        };
    }

    // Fail-open read. Every caller is a read-through cache over its own store,
    // so a Redis failure is a miss, never an error. When the multiplexer is
    // already disconnected, skip L2 outright: with AbortOnConnectFail=false
    // the command would otherwise sit in the backlog for the full 5 s timeout.
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        if (_local.TryGetValue(key, out T? hit) && hit is not null)
            return hit;

        if (!_redis.IsConnected)
            return null;

        RedisValue raw;
        try
        {
            raw = await _redis.GetDatabase().StringGetAsync(key);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _log.LogWarning("Redis read failed for cache key {Key} ({Error}: {Message}) — treating as a miss",
                key, ex.GetType().Name, ex.Message);
            return null;
        }

        if (raw.IsNullOrEmpty)
            return null;

        var value = Deserialize<T>(raw!);
        if (value is not null)
            _local.Set(key, value, L1Ttl);
        return value;
    }

    // L1 first, then return: the caller never waits on Redis. The L2 write and
    // the peer broadcast run in the background, still in that order.
    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);

        var l1Ttl = ttl < L1Ttl ? ttl : L1Ttl;
        _local.Set(key, value, l1Ttl);

        _ = WriteL2AndPublishAsync(key, json, ttl);
        return Task.CompletedTask;
    }

    private async Task WriteL2AndPublishAsync(string key, string json, TimeSpan ttl)
    {
        if (!_redis.IsConnected)
            return;

        try
        {
            await _redis.GetDatabase().StringSetAsync(key, json, ttl);

            // Broadcast peer-side invalidation so other replicas drop their
            // stale L1 copy of this key. We publish AFTER L2 is written so a
            // peer that responds to the message and re-reads sees the new
            // value, not the previous. Subscribers on this same pod filter
            // out the echo via the sender pod id.
            await PublishInvalidationAsync(key);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _log.LogWarning("Redis write failed for cache key {Key} ({Error}: {Message}) — kept in L1 only",
                key, ex.GetType().Name, ex.Message);
        }
    }

    // Deliberately NOT fail-open: callers invalidate after changing the
    // underlying data (credential rotation, token refresh, admin edits), and
    // an invalidation that silently missed L2 leaves peers on the old value.
    // The caller must see the failure; the key is also queued and replayed
    // when Redis comes back, so the stale L2 copy doesn't outlive the outage.
    public async Task InvalidateAsync(string key, CancellationToken ct = default)
    {
        _local.Remove(key);

        try
        {
            await DeleteAndBroadcastAsync(key);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _pendingInvalidations.TryAdd(key, 0);
            throw;
        }
    }

    private async Task DeleteAndBroadcastAsync(string key)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);

        // Broadcast — peers drop their L1 copies. Our own L1 was already
        // removed by the caller; the subscriber on this pod filters the
        // echo by sender id.
        await PublishInvalidationAsync(key);
    }

    private async Task ReplayPendingInvalidationsAsync()
    {
        foreach (var key in _pendingInvalidations.Keys)
        {
            try
            {
                await DeleteAndBroadcastAsync(key);
                _pendingInvalidations.TryRemove(key, out _);
                _log.LogInformation("Replayed cache invalidation for {Key} after Redis reconnect", key);
            }
            catch (Exception ex) when (IsRedisFailure(ex))
            {
                // Still down — the next ConnectionRestored retries it.
                _log.LogWarning("Replaying cache invalidation for {Key} failed ({Message}); will retry on next reconnect", key, ex.Message);
            }
        }
    }

    public async Task<T> GetOrSetWithLockAsync<T>(
        string key,
        string lockKey,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan ttl,
        TimeSpan lockTimeout,
        TimeSpan waitTimeout,
        CancellationToken ct = default) where T : class
    {
        var hit = await GetAsync<T>(key, ct);
        if (hit is not null)
            return hit;

        var db = _redis.GetDatabase();
        bool acquired = await db.StringSetAsync(lockKey, "1", lockTimeout, When.NotExists);

        if (acquired)
        {
            try
            {
                // Double-check — a peer may have populated the cache
                // between our miss above and our lock acquisition.
                var doubleCheck = await GetAsync<T>(key, ct);
                if (doubleCheck is not null)
                    return doubleCheck;

                var fresh = await factory(ct);
                if (fresh is null)
                    throw new InvalidOperationException(
                        $"Factory returned null for cache key '{key}'.");

                // Awaited L2 write, not SetAsync: waiters on other pods read
                // "lock released" as "value is in Redis", so the write must
                // land before the finally below drops the lock.
                _local.Set(key, fresh, ttl < L1Ttl ? ttl : L1Ttl);
                await WriteL2AndPublishAsync(key, JsonSerializer.Serialize(fresh, JsonOpts), ttl);
                return fresh;
            }
            finally
            {
                await db.KeyDeleteAsync(lockKey);
            }
        }

        // Lock contended — poll with jitter until the winner publishes
        // or waitTimeout elapses. Each iteration also checks whether the
        // lock has been released without a value being set, which means
        // the winner's factory threw; we break early in that case so the
        // caller sees a TimeoutException promptly instead of waiting the
        // full window. (We don't take over as the new holder here to
        // avoid unbounded recursion on a deterministic factory failure.)
        var deadline = Environment.TickCount64 + (long)waitTimeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            int jitter = 50 + System.Random.Shared.Next(50);
            await Task.Delay(TimeSpan.FromMilliseconds(jitter), ct);

            var late = await GetAsync<T>(key, ct);
            if (late is not null)
                return late;

            // Lock released without a value cached → previous holder
            // failed. No point waiting further on this attempt.
            bool lockStillHeld = await db.KeyExistsAsync(lockKey);
            if (!lockStillHeld)
                break;
        }

        throw new TimeoutException(
            $"Cache fill timed out for key '{key}' after {waitTimeout.TotalMilliseconds}ms " +
            $"waiting on lock '{lockKey}'.");
    }

    private async Task PublishInvalidationAsync(string key)
    {
        var payload = JsonSerializer.Serialize(
            new InvalidationMessage(key, _pod.Id), JsonOpts);
        var sub = _redis.GetSubscriber();
        await sub.PublishAsync(RedisChannel.Literal(InvalidationChannel), payload);
    }

    // RedisTimeoutException derives from TimeoutException, NOT RedisException
    // — a plain `catch (RedisException)` misses the very timeout seen on
    // 2026-10-01 (GET iam:cred:wms, 5008 ms). ObjectDisposedException covers
    // a multiplexer torn down during shutdown.
    private static bool IsRedisFailure(Exception ex) =>
        ex is RedisException or RedisTimeoutException or ObjectDisposedException;

    private T? Deserialize<T>(string raw) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOpts);
        }
        catch (JsonException ex)
        {
            // Corrupt/legacy payload — log + treat as miss. Caller will
            // refetch and overwrite.
            _log.LogWarning(ex, "Failed to deserialize cached value as {Type}", typeof(T).Name);
            return null;
        }
    }

    public sealed record InvalidationMessage(string Key, string PodId);
}
