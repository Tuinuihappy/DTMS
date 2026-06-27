using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Drain;

// G1 Phase 1 — reject NEW SignalR connections + invocations while draining.
// Belt-and-suspenders alongside the DrainHealthCheck:
//   - DrainHealthCheck flips /health/ready to 503 so the K8s service mesh
//     stops routing NEW traffic. But races exist: a client could be mid-
//     reconnect when the readiness flip happens, or service mesh routing
//     may have a propagation delay.
//   - DrainAwareHubFilter is the in-process guard. Any reconnect attempt
//     that slipped past the readiness flip gets a HubException with a
//     "server is draining" message; the SignalR client interprets this as
//     a connection error and falls back to its automatic-reconnect logic,
//     which the K8s service mesh will then route to a healthy pod.
//
// Registered globally on all hubs via `options.AddFilter<...>()` in
// Program.cs AddSignalR setup, same pattern as TracingHubFilter +
// RateLimitedHubFilter.
internal sealed class DrainAwareHubFilter : IHubFilter
{
    private readonly IConnectionDrainService _drain;

    public DrainAwareHubFilter(IConnectionDrainService drain)
    {
        _drain = drain;
    }

    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (_drain.IsDraining)
        {
            throw new HubException("server is draining — please reconnect");
        }
        return next(invocationContext);
    }

    public Task OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next)
    {
        if (_drain.IsDraining)
        {
            throw new HubException("server is draining — please connect to a different pod");
        }
        return next(context);
    }

    public Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        // Always let disconnections proceed — the whole point is to drain.
        return next(context, exception);
    }
}
