namespace AMR.DeliveryPlanning.SharedKernel.Projection;

/// <summary>
/// Replay orchestration contract — given a projector name + time window,
/// re-feed historical events into the projector so the read model is
/// rebuilt from scratch. Used after fixing a projector bug or recovering
/// from a corrupted read-model state.
///
/// P0 ships the contract only. A real implementation needs:
///   - access to the event archive (outbox table or cold storage)
///   - per-projector clear/truncate of the affected read model
///   - progress reporting + cancellation
///
/// These are deferred until the first real replay is needed (see
/// docs/projection-conventions.md §10 — Deferred from P0).
/// </summary>
public interface IProjectionReplayService
{
    /// <summary>
    /// Replay all events for the given projector within the time window.
    /// When <paramref name="aggregateId"/> is provided, only events for
    /// that aggregate are replayed (targeted fix); otherwise every event
    /// in the window is replayed (full rebuild).
    /// </summary>
    /// <param name="projectorName">
    /// The <see cref="IdempotentProjector{TEvent}.ProjectorName"/> of the
    /// projector to replay. Used to scope the inbox clear + event filter.
    /// </param>
    /// <param name="fromUtc">Inclusive start of replay window.</param>
    /// <param name="toUtc">Exclusive end of replay window. Defaults to now.</param>
    /// <param name="aggregateId">
    /// Optional — limit replay to one aggregate. When null, replay every
    /// aggregate in the window.
    /// </param>
    /// <returns>
    /// Replay summary including events processed, skipped, failed.
    /// </returns>
    Task<ReplaySummary> ReplayAsync(
        string projectorName,
        DateTime fromUtc,
        DateTime? toUtc = null,
        Guid? aggregateId = null,
        CancellationToken cancellationToken = default);
}

public record ReplaySummary(
    string ProjectorName,
    DateTime FromUtc,
    DateTime ToUtc,
    int EventsProcessed,
    int EventsSkipped,
    int EventsFailed,
    TimeSpan Elapsed);

/// <summary>
/// Default implementation that throws — replaced by a real implementation
/// once the operational need arises (per docs/projection-conventions.md
/// §10). Keeping the type registered means DI wiring is in place from day
/// one, so adopting the real implementation later is a swap, not a wire-up.
/// </summary>
public sealed class NotImplementedReplayService : IProjectionReplayService
{
    public Task<ReplaySummary> ReplayAsync(
        string projectorName, DateTime fromUtc, DateTime? toUtc = null,
        Guid? aggregateId = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException(
            "Projection replay is not yet implemented — see docs/projection-conventions.md §10.");
}
