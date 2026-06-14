namespace AMR.DeliveryPlanning.Dispatch.Application.Projections;

/// <summary>
/// Phase P1 — abstraction so TripStatusHistoryProjector can push timeline
/// entries to the API project's <c>TripHub</c> without taking a hard
/// dependency on SignalR. Same pattern as the Order/Job publishers; see
/// the Order interface for the rationale comment.
/// </summary>
public interface ITripRealtimePublisher
{
    Task PublishTimelineUpdatedAsync(Guid tripId, TripTimelineEntryDto entry, CancellationToken cancellationToken = default);
}

public sealed record TripTimelineEntryDto(
    Guid EventId,
    Guid TripId,
    Guid? DeliveryOrderId,
    Guid? JobId,
    string? FromStatus,
    string ToStatus,
    DateTime OccurredAt,
    string? Reason);

public sealed class NoopTripRealtimePublisher : ITripRealtimePublisher
{
    public Task PublishTimelineUpdatedAsync(
        Guid tripId, TripTimelineEntryDto entry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
