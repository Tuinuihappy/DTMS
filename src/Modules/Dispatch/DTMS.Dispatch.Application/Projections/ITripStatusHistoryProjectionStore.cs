namespace AMR.DeliveryPlanning.Dispatch.Application.Projections;

/// <summary>
/// Write-side abstraction consumed by <c>TripStatusHistoryProjector</c>.
/// Returns the latest row's <c>(ToStatus, OccurredAt, DeliveryOrderId,
/// JobId)</c> tuple so the projector can derive FromStatus and reuse the
/// order/job ids on events whose payload doesn't carry them
/// (TripPaused/TripResumed).
/// </summary>
public interface ITripStatusHistoryProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);

    Task<TripHistoryLatest?> GetLatestForTripAsync(Guid tripId, CancellationToken cancellationToken = default);

    Task AppendAsync(
        string projectorName,
        Guid eventId,
        Guid tripId,
        Guid? deliveryOrderId,
        Guid? jobId,
        string? fromStatus,
        string toStatus,
        DateTime occurredAt,
        string? reason,
        CancellationToken cancellationToken = default);
}

public record TripHistoryLatest(
    string ToStatus,
    DateTime OccurredAt,
    Guid? DeliveryOrderId,
    Guid? JobId);
