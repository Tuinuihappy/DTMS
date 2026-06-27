using AMR.DeliveryPlanning.Api.Realtime.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Drain;

// G1 Phase 1 — singleton drain coordinator. Three jobs:
//   1. Flip IsDraining once (first writer wins via Interlocked) so
//      DrainHealthCheck flips /health/ready to 503 and the K8s service
//      mesh stops routing new traffic to this pod.
//   2. Broadcast a graceful "__drain" event to every hub so existing
//      SignalR clients can reconnect to a different pod without seeing
//      a Disconnected error.
//   3. Wait for the configured settle window so the broadcast + reconnect
//      can complete before SIGTERM lands.
//
// Hub list is inlined here (5 hubs as of 2026-06-21). Phase 2 will
// generalise this via IDrainBroadcaster<THub> + DI enumeration so adding
// a new hub doesn't require touching this file. For Phase 1 the simpler
// explicit list is enough — easier to read, easier to debug, no
// reflection magic at boot.
public sealed class ConnectionDrainService : IConnectionDrainService
{
    private int _draining;                 // 0/1 atomic — first writer wins
    private DateTimeOffset? _startedAt;

    private readonly IHubContext<OrderHub> _orderHub;
    private readonly IHubContext<JobHub> _jobHub;
    private readonly IHubContext<TripHub> _tripHub;
    private readonly IHubContext<DashboardHub> _dashboardHub;
    private readonly IHubContext<FleetHub> _fleetHub;
    private readonly ILogger<ConnectionDrainService> _logger;

    public ConnectionDrainService(
        IHubContext<OrderHub> orderHub,
        IHubContext<JobHub> jobHub,
        IHubContext<TripHub> tripHub,
        IHubContext<DashboardHub> dashboardHub,
        IHubContext<FleetHub> fleetHub,
        ILogger<ConnectionDrainService> logger)
    {
        _orderHub = orderHub;
        _jobHub = jobHub;
        _tripHub = tripHub;
        _dashboardHub = dashboardHub;
        _fleetHub = fleetHub;
        _logger = logger;
    }

    public bool IsDraining => Interlocked.CompareExchange(ref _draining, 0, 0) == 1;

    public DateTimeOffset? StartedAt => _startedAt;

    public async Task StartDrainAsync(TimeSpan settleWindow, CancellationToken cancellationToken = default)
    {
        // Idempotency: first writer wins. Second + later calls observe
        // IsDraining=true and short-circuit. Crucially we DON'T re-broadcast
        // or re-wait — that would extend shutdown unnecessarily and confuse
        // metrics.
        if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
        {
            _logger.LogInformation(
                "[Drain] StartDrainAsync called again — already draining since {StartedAt}",
                _startedAt);
            return;
        }
        _startedAt = DateTimeOffset.UtcNow;

        _logger.LogWarning(
            "[Drain] beginning connection drain — pod={Pod} settle window {SettleSeconds}s",
            PodGroupHubFilter.PodGroupKey, settleWindow.TotalSeconds);

        // Broadcast "__drain" to every hub. Each SendAsync is wrapped in
        // try/catch independently so a failed broadcast on one hub doesn't
        // skip the others. The frontend's SignalR client treats this as
        // "reconnect now" (Phase 3 implements that handler); without
        // Phase 3 it's a no-op event the client ignores, which is fine
        // — the readiness flip (DrainHealthCheck) + new-connection
        // rejection (DrainAwareHubFilter) still give most of the value.
        await BroadcastDrainSafely("OrderHub", _orderHub, cancellationToken);
        await BroadcastDrainSafely("JobHub", _jobHub, cancellationToken);
        await BroadcastDrainSafely("TripHub", _tripHub, cancellationToken);
        await BroadcastDrainSafely("DashboardHub", _dashboardHub, cancellationToken);
        await BroadcastDrainSafely("FleetHub", _fleetHub, cancellationToken);

        // Settle window: give clients time to receive "__drain" + reconnect
        // elsewhere. The host's overall shutdown budget (Host.ShutdownTimeout +
        // stop_grace_period) owns the upper bound on this — caller should
        // pick a value comfortably under those.
        try
        {
            await Task.Delay(settleWindow, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Drain] settle window cancelled before completion — host shutting down");
            return;
        }

        _logger.LogWarning("[Drain] settle window elapsed — host shutdown will follow");
    }

    private async Task BroadcastDrainSafely<THub>(
        string hubName,
        IHubContext<THub> hub,
        CancellationToken ct)
        where THub : Hub
    {
        try
        {
            // Phase F1 follow-up — target THIS pod's clients only.
            // Clients.All would fan out via the Redis backplane to every
            // pod in the cluster, asking every client (not just this
            // pod's) to reconnect. That makes a rolling-deploy drain a
            // cluster-wide reconnect storm — the exact failure mode G1
            // was built to prevent. PodGroupHubFilter joins each new
            // connection to a group keyed by this pod's hostname; we
            // broadcast only to that group.
            await hub.Clients
                .Group(PodGroupHubFilter.PodGroupKey)
                .SendAsync("__drain", cancellationToken: ct);
            _logger.LogInformation(
                "[Drain] broadcast __drain to {Hub} group={Group}",
                hubName, PodGroupHubFilter.PodGroupKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[Drain] broadcast to {Hub} failed — proceeding anyway", hubName);
        }
    }
}
