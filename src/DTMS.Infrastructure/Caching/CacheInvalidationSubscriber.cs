using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DTMS.Infrastructure.Caching;

/// <summary>
/// Listens on the Redis invalidation channel and drops the named key from
/// this pod's L1 cache. Pairs with <see cref="RedisBackedTieredCache"/>'s
/// SetAsync + InvalidateAsync, both of which publish a small JSON envelope
/// carrying the key plus the sender's pod id. Messages from this same pod
/// are skipped — without the filter, a SetAsync would publish, immediately
/// receive its own echo, and evict the entry it had just populated.
/// </summary>
public sealed class CacheInvalidationSubscriber : IHostedService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly IMemoryCache _local;
    private readonly PodIdentity _pod;
    private readonly ILogger<CacheInvalidationSubscriber> _log;
    private ChannelMessageQueue? _queue;

    public CacheInvalidationSubscriber(
        IConnectionMultiplexer redis,
        IMemoryCache local,
        PodIdentity pod,
        ILogger<CacheInvalidationSubscriber> log)
    {
        _redis = redis;
        _local = local;
        _pod = pod;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var sub = _redis.GetSubscriber();
        _queue = await sub.SubscribeAsync(
            RedisChannel.Literal(RedisBackedTieredCache.InvalidationChannel));

        _queue.OnMessage(msg =>
        {
            string? payload = msg.Message;
            if (string.IsNullOrEmpty(payload))
                return;

            try
            {
                var parsed = JsonSerializer.Deserialize<RedisBackedTieredCache.InvalidationMessage>(
                    payload, JsonOpts);
                if (parsed is null || string.IsNullOrEmpty(parsed.Key))
                    return;

                // Skip our own echo — without this, every SetAsync on
                // this pod would race against its own publish and evict
                // the entry it just populated.
                if (parsed.PodId == _pod.Id)
                    return;

                _local.Remove(parsed.Key);
            }
            catch (Exception ex)
            {
                // Subscriber callbacks must never throw — StackExchange.Redis
                // will tear down the subscription otherwise.
                _log.LogWarning(ex,
                    "Failed to process cache invalidation payload: {Payload}",
                    payload);
            }
        });

        _log.LogInformation(
            "Subscribed to cache invalidation channel {Channel} as pod {PodId}",
            RedisBackedTieredCache.InvalidationChannel, _pod.Id);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_queue is not null)
            await _queue.UnsubscribeAsync();
    }
}
