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
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        if (_local.TryGetValue(key, out T? hit) && hit is not null)
            return hit;

        var db = _redis.GetDatabase();
        RedisValue raw = await db.StringGetAsync(key);
        if (raw.IsNullOrEmpty)
            return null;

        var value = Deserialize<T>(raw!);
        if (value is not null)
            _local.Set(key, value, L1Ttl);
        return value;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);
        var db = _redis.GetDatabase();
        await db.StringSetAsync(key, json, ttl);

        var l1Ttl = ttl < L1Ttl ? ttl : L1Ttl;
        _local.Set(key, value, l1Ttl);

        // Broadcast peer-side invalidation so other replicas drop their
        // stale L1 copy of this key. We publish AFTER L2 is written so a
        // peer that responds to the message and re-reads sees the new
        // value, not the previous. Subscribers on this same pod filter
        // out the echo via the sender pod id.
        await PublishInvalidationAsync(key);
    }

    public async Task InvalidateAsync(string key, CancellationToken ct = default)
    {
        _local.Remove(key);

        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);

        // Broadcast — peers drop their L1 copies. Our own L1 was already
        // removed above; the subscriber on this pod filters the echo by
        // sender id.
        await PublishInvalidationAsync(key);
    }

    public async Task<T> GetOrSetWithLockAsync<T>(
        string key,
        string lockKey,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan ttl,
        TimeSpan lockTimeout,
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

                await SetAsync(key, fresh, ttl, ct);
                return fresh;
            }
            finally
            {
                await db.KeyDeleteAsync(lockKey);
            }
        }

        // Lock contended — poll with jitter until the winner publishes.
        // Bounded retry: 10 × ~75ms = ~750ms worst case before we give up.
        for (int i = 0; i < 10; i++)
        {
            int jitter = 50 + System.Random.Shared.Next(50);
            await Task.Delay(TimeSpan.FromMilliseconds(jitter), ct);

            var late = await GetAsync<T>(key, ct);
            if (late is not null)
                return late;
        }

        throw new TimeoutException(
            $"Cache fill timed out for key '{key}' after waiting on lock '{lockKey}'.");
    }

    private async Task PublishInvalidationAsync(string key)
    {
        var payload = JsonSerializer.Serialize(
            new InvalidationMessage(key, _pod.Id), JsonOpts);
        var sub = _redis.GetSubscriber();
        await sub.PublishAsync(RedisChannel.Literal(InvalidationChannel), payload);
    }

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
