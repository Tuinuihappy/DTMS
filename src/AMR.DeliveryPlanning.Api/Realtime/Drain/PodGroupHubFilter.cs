using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Drain;

// G1 + Phase F1 follow-up — auto-join every new hub connection to a
// per-pod group ("pod:{hostname}"). The drain broadcast in
// ConnectionDrainService then targets THIS pod's group instead of
// Clients.All — which after the Redis backplane shipped (Phase F1)
// fans out to every pod in the cluster, defeating the whole point of
// G1: drain is supposed to ask THIS pod's clients to reconnect to a
// sibling, not stampede the entire cluster on every rolling-deploy step.
//
// Trade-off: Clients.Group still publishes via the backplane (other
// pods receive "send to group pod:abc", look for local connections,
// find none, drop). That's one Redis round-trip per drain instead of
// zero — negligible since drain happens once per pod lifetime.
//
// Hostname source: Environment.MachineName. In Docker compose this is
// the container ID prefix; in K8s it's the pod name. Stable + unique
// per pod = exactly what we need.
internal sealed class PodGroupHubFilter : IHubFilter
{
    /// <summary>SignalR group every connection on this pod belongs to.</summary>
    public static readonly string PodGroupKey = $"pod:{Environment.MachineName}";

    private readonly ILogger<PodGroupHubFilter> _logger;

    public PodGroupHubFilter(ILogger<PodGroupHubFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next)
    {
        // Run downstream first so the connection is fully established
        // before we touch its group membership — avoids races where a
        // very-early invocation could see partial state.
        await next(context);
        try
        {
            await context.Hub.Groups.AddToGroupAsync(
                context.Context.ConnectionId, PodGroupKey);
        }
        catch (Exception ex)
        {
            // Don't fail the connection if the group add itself errors —
            // the worst that happens is this connection misses the drain
            // broadcast and falls back to SignalR's auto-reconnect path
            // when SIGTERM lands. Logged so it's diagnosable but not
            // fatal.
            _logger.LogWarning(ex,
                "[PodGroup] failed to add {ConnectionId} to {Group} — drain may not reach this connection",
                context.Context.ConnectionId, PodGroupKey);
        }
    }

    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
        => next(invocationContext);

    public Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
        // SignalR auto-removes the connection from all groups on
        // disconnect — no cleanup needed here.
        => next(context, exception);
}
