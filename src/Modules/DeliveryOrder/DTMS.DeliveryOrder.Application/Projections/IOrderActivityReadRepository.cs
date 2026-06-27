namespace DTMS.DeliveryOrder.Application.Projections;

public record OrderActivityEntry(
    Guid Id,
    Guid EventId,
    Guid OrderId,
    string Category,
    string EventType,
    string? Details,
    string? ActorId,
    DateTime OccurredAt,
    Guid? RelatedTripId,
    int? AttemptNumber);

public interface IOrderActivityReadRepository
{
    /// <summary>
    /// All activity rows for an order, newest first. Empty for unknown
    /// or pre-backfill orders (the seed SQL covers existing data).
    /// </summary>
    Task<IReadOnlyList<OrderActivityEntry>> GetForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default);
}
