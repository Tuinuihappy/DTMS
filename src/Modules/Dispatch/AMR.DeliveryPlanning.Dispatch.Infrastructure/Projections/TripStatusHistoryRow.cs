namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Projections;

/// <summary>
/// Phase P1 read-model row materialized by <c>TripStatusHistoryProjector</c>
/// from Dispatch integration events. Mirrors the shape of Order/Job
/// counterparts so a single shared <c>&lt;TimelineView /&gt;</c> renders
/// either.
///
/// DeliveryOrderId is nullable here (unlike Job's) because TripPaused
/// and TripResumed events don't carry it; the projector usually backfills
/// it from the prior row but allows null at the boundary so a misordered
/// pause-before-start (rare) doesn't NPE.
/// </summary>
public class TripStatusHistoryRow
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid TripId { get; private set; }
    public Guid? DeliveryOrderId { get; private set; }
    public Guid? JobId { get; private set; }
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = string.Empty;
    public DateTime OccurredAt { get; private set; }
    public string? Reason { get; private set; }

    private TripStatusHistoryRow() { }   // EF

    public TripStatusHistoryRow(
        Guid eventId, Guid tripId, Guid? deliveryOrderId, Guid? jobId,
        string? fromStatus, string toStatus, DateTime occurredAt, string? reason)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("EventId is required.", nameof(eventId));
        if (tripId == Guid.Empty)
            throw new ArgumentException("TripId is required.", nameof(tripId));
        if (string.IsNullOrWhiteSpace(toStatus))
            throw new ArgumentException("ToStatus is required.", nameof(toStatus));

        Id = Guid.NewGuid();
        EventId = eventId;
        TripId = tripId;
        DeliveryOrderId = deliveryOrderId;
        JobId = jobId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        OccurredAt = occurredAt;
        Reason = reason;
    }
}
