using System.Diagnostics;
using AMR.DeliveryPlanning.Api.Realtime.Observability;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Filters;

/// <summary>
/// Cross-cutting tracing + metrics for every hub method invocation.
///
/// On invoke:
///   - Open an Activity (OpenTelemetry trace span) named "hub.{method}"
///   - Tag with hub name, connection id, user id
///   - Record method invocation count + duration histogram
///   - Re-throws transparently so other filters / SignalR error handling
///     still run.
///
/// On connect:
///   - Bump connections counter for the hub.
///
/// Registered as a singleton — no per-invocation state, safe to share.
/// </summary>
public sealed class TracingHubFilter : IHubFilter
{
    private static readonly ActivitySource Source = new("DTMS.SignalR");
    private readonly HubMetrics _metrics;
    private readonly ILogger<TracingHubFilter> _logger;

    public TracingHubFilter(HubMetrics metrics, ILogger<TracingHubFilter> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext ctx,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var hubName = ctx.Hub.GetType().Name;
        using var activity = Source.StartActivity($"hub.{hubName}.{ctx.HubMethodName}");
        activity?.SetTag("signalr.hub", hubName);
        activity?.SetTag("signalr.method", ctx.HubMethodName);
        activity?.SetTag("signalr.connection_id", ctx.Context.ConnectionId);
        activity?.SetTag("signalr.user_id", ctx.Context.UserIdentifier);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await next(ctx);
            sw.Stop();
            _metrics.RecordInvocation(hubName, ctx.HubMethodName, sw.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _metrics.RecordInvocation(hubName, ctx.HubMethodName, sw.Elapsed.TotalMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogWarning(ex,
                "Hub method failure: {Hub}.{Method} (connection={ConnectionId})",
                hubName, ctx.HubMethodName, ctx.Context.ConnectionId);
            throw;
        }
    }

    public Task OnConnectedAsync(
        HubLifetimeContext ctx,
        Func<HubLifetimeContext, Task> next)
    {
        _metrics.RecordConnected(ctx.Hub.GetType().Name);
        return next(ctx);
    }

    public Task OnDisconnectedAsync(
        HubLifetimeContext ctx,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
        => next(ctx, exception);
}
