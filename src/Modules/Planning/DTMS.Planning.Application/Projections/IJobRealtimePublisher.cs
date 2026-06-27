namespace AMR.DeliveryPlanning.Planning.Application.Projections;

/// <summary>
/// Phase P1 — abstraction so JobStatusHistoryProjector can push timeline
/// entries to the API project's <c>JobHub</c> without taking a hard
/// dependency on SignalR. Same pattern as the Order/Trip publishers; see
/// the Order interface for the rationale comment.
/// </summary>
public interface IJobRealtimePublisher
{
    Task PublishTimelineUpdatedAsync(Guid jobId, JobTimelineEntryDto entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase P3 quick-win — hint that the cross-order jobs queue has
    /// changed for the given job. Payload is intentionally minimal — the
    /// frontend treats it as a refetch trigger rather than as queue-row
    /// state to merge. Avoids the consumer needing to materialise a full
    /// JobDto inside the projector hot path (lazy fetch from /jobs/queue
    /// is cheaper + simpler than juggling row identity here).
    /// </summary>
    Task PublishJobQueueChangedAsync(Guid jobId, string toStatus, CancellationToken cancellationToken = default);
}

public sealed record JobTimelineEntryDto(
    Guid EventId,
    Guid JobId,
    Guid? DeliveryOrderId,
    string? FromStatus,
    string ToStatus,
    DateTime OccurredAt,
    string? Reason);

public sealed class NoopJobRealtimePublisher : IJobRealtimePublisher
{
    public Task PublishTimelineUpdatedAsync(
        Guid jobId, JobTimelineEntryDto entry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task PublishJobQueueChangedAsync(
        Guid jobId, string toStatus, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
