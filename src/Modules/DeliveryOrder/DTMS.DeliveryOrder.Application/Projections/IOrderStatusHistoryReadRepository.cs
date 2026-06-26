namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Read side of the OrderStatusHistory projection — consumed by the
/// status-history query handler. Application doesn't see the EF row type
/// directly; the infrastructure layer projects it to this DTO at the
/// query boundary, keeping Application unaware of EF.
/// </summary>
public record OrderStatusHistoryEntry(
    Guid EventId,
    Guid OrderId,
    string? FromStatus,
    string ToStatus,
    DateTime OccurredAt,
    string? Reason);

public interface IOrderStatusHistoryReadRepository
{
    /// <summary>
    /// All transitions for an order, newest first.
    /// Returns empty list (not null) for unknown orders + pre-projection
    /// legacy orders that haven't been backfilled yet.
    /// </summary>
    Task<IReadOnlyList<OrderStatusHistoryEntry>> GetForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Most recent transition for the order — used by the projector to
    /// derive FromStatus from previous ToStatus and to drop out-of-order
    /// events (when the new event's OccurredAt is older than the latest).
    /// </summary>
    Task<OrderStatusHistoryEntry?> GetLatestForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default);
}
