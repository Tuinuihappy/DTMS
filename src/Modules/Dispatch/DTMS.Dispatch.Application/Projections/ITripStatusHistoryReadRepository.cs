namespace DTMS.Dispatch.Application.Projections;

public record TripStatusHistoryEntry(
    Guid EventId,
    Guid TripId,
    Guid? DeliveryOrderId,
    Guid? JobId,
    string? FromStatus,
    string ToStatus,
    DateTime OccurredAt,
    string? Reason);

public interface ITripStatusHistoryReadRepository
{
    Task<IReadOnlyList<TripStatusHistoryEntry>> GetForTripAsync(
        Guid tripId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Union of every trip's history rows for the given delivery order,
    /// newest first. Powers the per-order trip rollup.
    /// </summary>
    Task<IReadOnlyList<TripStatusHistoryEntry>> GetForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default);
}
