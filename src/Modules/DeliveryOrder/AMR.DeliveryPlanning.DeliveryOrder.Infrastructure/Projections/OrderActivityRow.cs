namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Projections;

/// <summary>
/// Phase P2 read-model row materialized by <c>OrderActivityProjector</c>.
/// One row per order activity (status transition, trip event, amendment,
/// POD scan, OMS notify, trip retry, etc.). Replaces the runtime 4-source
/// UNION in <c>GetFullOrderAuditQueryHandler</c>.
///
/// <para>Category discriminator mirrors the old <c>Source</c> string so
/// the UI doesn't need to relearn the taxonomy:</para>
/// <list type="bullet">
///   <item><b>OrderLifecycle</b> — Confirmed/Held/Released/Cancelled/etc.</item>
///   <item><b>Amendment</b> — service-window edits, metadata changes</item>
///   <item><b>TripExecution</b> — vendor lifecycle per trip (Started, Pickup, Drop, Completed, Failed, Cancelled, Paused, Resumed, ExceptionRaised)</item>
///   <item><b>TripRetry</b> — every retry attempt's trigger</item>
///   <item><b>Pod</b> — POD scans (item-level proof of delivery)</item>
///   <item><b>OmsNotify</b> — upstream OMS notifications fired/succeeded/failed</item>
///   <item><b>Order</b> — generic catch-all from OrderAuditEvent backfill</item>
/// </list>
///
/// <para>RelatedTripId + AttemptNumber are denormalized onto every row so
/// the UI can link / group without a separate trip lookup. <c>Payload</c>
/// is jsonb so category-specific extensions don't require migrations.</para>
/// </summary>
public class OrderActivityRow
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid OrderId { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string? Details { get; private set; }
    public string? ActorId { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public Guid? RelatedTripId { get; private set; }
    public int? AttemptNumber { get; private set; }

    private OrderActivityRow() { }   // EF

    public OrderActivityRow(
        Guid eventId, Guid orderId,
        string category, string eventType,
        string? details, string? actorId,
        DateTime occurredAt,
        Guid? relatedTripId, int? attemptNumber)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("EventId is required.", nameof(eventId));
        if (orderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category is required.", nameof(category));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType is required.", nameof(eventType));

        Id = Guid.NewGuid();
        EventId = eventId;
        OrderId = orderId;
        Category = category;
        EventType = eventType;
        Details = details;
        ActorId = actorId;
        OccurredAt = occurredAt;
        RelatedTripId = relatedTripId;
        AttemptNumber = attemptNumber;
    }
}
