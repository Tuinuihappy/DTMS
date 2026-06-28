namespace DTMS.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P2 write-side abstraction consumed by <c>OrderActivityProjector</c>.
/// Mirrors the OrderStatusHistory store shape so the projector can use the
/// same dedup + append pattern proven in P1.
/// </summary>
public interface IOrderActivityProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);

    Task AppendAsync(
        string projectorName,
        Guid eventId,
        Guid orderId,
        string category,
        string eventType,
        string? details,
        string? actorId,
        DateTime occurredAt,
        Guid? relatedTripId,
        int? attemptNumber,
        string? channel = null,
        string? displayName = null,
        CancellationToken cancellationToken = default);
}
