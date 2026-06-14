using System.Collections.Concurrent;
using AMR.DeliveryPlanning.Api.Realtime.Observability;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Filters;

/// <summary>
/// Per-connection rate limiting for hub method invocations. Defends
/// against:
///   - A misbehaving client subscribing in a loop.
///   - Compromised credentials hammering Subscribe/Unsubscribe.
///   - Accidental tight loops in dev that would otherwise pile messages
///     onto the message bus.
///
/// Defaults to <b>100 invocations per second burst, sustained 20/s</b>
/// — generous for normal UI flow (subscribe-on-open, unsubscribe-on-close)
/// but tight enough to halt a runaway loop within seconds.
///
/// On exceed: throws <see cref="HubException"/> so the client's
/// <c>invoke()</c> rejects with a clear "Rate limit exceeded" message.
/// The connection itself stays open — the next call inside the window
/// will pass once tokens refill.
/// </summary>
public sealed class RateLimitedHubFilter : IHubFilter
{
    private const int CapacityBurst = 100;
    private const int SustainedPerSecond = 20;

    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
    private readonly HubMetrics _metrics;

    public RateLimitedHubFilter(HubMetrics metrics)
    {
        _metrics = metrics;
    }

    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext ctx,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        // GetOrAdd handles the race where InvokeMethodAsync fires before
        // OnConnectedAsync wires the bucket (rare but possible under load).
        var bucket = _buckets.GetOrAdd(
            ctx.Context.ConnectionId,
            _ => new TokenBucket(CapacityBurst, SustainedPerSecond));

        if (!bucket.TryConsume())
        {
            var hubName = ctx.Hub.GetType().Name;
            _metrics.RecordRateLimited(hubName, ctx.HubMethodName);
            throw new HubException(
                $"Rate limit exceeded for {hubName}.{ctx.HubMethodName}. " +
                $"Limit: burst {CapacityBurst}, sustained {SustainedPerSecond}/s.");
        }

        return next(ctx);
    }

    public Task OnConnectedAsync(
        HubLifetimeContext ctx,
        Func<HubLifetimeContext, Task> next)
    {
        _buckets[ctx.Context.ConnectionId] =
            new TokenBucket(CapacityBurst, SustainedPerSecond);
        return next(ctx);
    }

    public Task OnDisconnectedAsync(
        HubLifetimeContext ctx,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        // Bucket dictionary grows unboundedly without this — every
        // disconnected connection leaks until process restart.
        _buckets.TryRemove(ctx.Context.ConnectionId, out _);
        return next(ctx, exception);
    }
}
