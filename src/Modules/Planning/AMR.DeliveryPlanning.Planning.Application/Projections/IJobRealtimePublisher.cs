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
}
