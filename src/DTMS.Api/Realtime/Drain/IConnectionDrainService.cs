namespace DTMS.Api.Realtime.Drain;

// G1 Phase 1 — pod drain coordination. Lives in DI as a singleton so the
// admin endpoint, the health check, and the hub filter all read the same
// drain state. Forward-only by design: once StartDrain has been called,
// the pod is committed to terminate — we never flip back to "healthy"
// because the K8s preStop hook has already taken this pod out of the
// service mesh's rotation.
public interface IConnectionDrainService
{
    /// <summary>True once StartDrainAsync has been called. Never resets.</summary>
    bool IsDraining { get; }

    /// <summary>Started timestamp (UTC) or null if not draining yet.</summary>
    DateTimeOffset? StartedAt { get; }

    /// <summary>
    /// Begin draining. Idempotent — calling twice does the same as once.
    /// Returns a Task that completes after the broadcast + initial settle
    /// window. Plumbed to K8s preStop via POST /api/v1/admin/drain-start.
    /// </summary>
    Task StartDrainAsync(TimeSpan settleWindow, CancellationToken cancellationToken = default);
}
