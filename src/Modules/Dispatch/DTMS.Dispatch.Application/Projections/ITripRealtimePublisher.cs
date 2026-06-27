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

    Task PublishMissionUpdatedAsync(Guid tripId, TripMissionEventDto entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Backend Phase 2 — broadcast a refetch hint to the trips-list group
    /// after a durable status transition. Payload mirrors
    /// <c>IOrderRealtimePublisher.PublishOrderListChangedAsync</c>:
    /// <c>{ tripId, toStatus }</c>, just enough for the dispatcher table
    /// to debounce-refetch.
    /// </summary>
    Task PublishTripListChangedAsync(Guid tripId, string toStatus, CancellationToken cancellationToken = default);
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

// Wire shape for the MissionUpdated SignalR callback. Field set mirrors
// TripMissionDto (returned by /trips/{id}/details) so the frontend can
// merge a pushed event straight into the existing missions array.
public sealed record TripMissionEventDto(
    int MissionIndex,
    string MissionKey,
    string MissionType,
    string State,
    string? StationName,
    string? ActionName,
    string? ActionType,
    string? ResultCode,
    string? ErrorMessage,
    DateTime ChangeStateTime,
    DateTime ReceivedAt);

public sealed class NoopTripRealtimePublisher : ITripRealtimePublisher
{
    public Task PublishTimelineUpdatedAsync(
        Guid tripId, TripTimelineEntryDto entry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task PublishMissionUpdatedAsync(
        Guid tripId, TripMissionEventDto entry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task PublishTripListChangedAsync(
        Guid tripId, string toStatus, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
