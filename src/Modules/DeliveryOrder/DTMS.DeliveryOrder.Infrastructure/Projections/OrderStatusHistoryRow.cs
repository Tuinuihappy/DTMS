namespace DTMS.DeliveryOrder.Infrastructure.Projections;

/// <summary>
/// Phase P1 read-model row materialized by <c>OrderStatusHistoryProjector</c>
/// from DeliveryOrder integration events. One row per observed status
/// transition for a given order, sorted by OccurredAt.
///
/// FromStatus is null for the first row of an aggregate (decision logged
/// in docs/event-projection-plan.md): cheaper than a read-modify-write
/// inside the projector and avoids the race that would force per-aggregate
/// ordering at the bus level.
///
/// Owned by the projection layer — write side never touches this table.
/// Replaying the projector against the same event stream MUST produce
/// identical rows (deterministic — see projection-conventions.md §2).
/// </summary>
public class OrderStatusHistoryRow
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid OrderId { get; private set; }
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = string.Empty;
    public DateTime OccurredAt { get; private set; }
    public string? Reason { get; private set; }

    private OrderStatusHistoryRow() { }   // EF

    public OrderStatusHistoryRow(
        Guid eventId, Guid orderId, string? fromStatus, string toStatus,
        DateTime occurredAt, string? reason)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("EventId is required.", nameof(eventId));
        if (orderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        if (string.IsNullOrWhiteSpace(toStatus))
            throw new ArgumentException("ToStatus is required.", nameof(toStatus));

        Id = Guid.NewGuid();
        EventId = eventId;
        OrderId = orderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        OccurredAt = occurredAt;
        Reason = reason;
    }
}
