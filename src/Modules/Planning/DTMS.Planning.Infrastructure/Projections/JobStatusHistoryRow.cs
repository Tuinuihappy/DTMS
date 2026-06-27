namespace AMR.DeliveryPlanning.Planning.Infrastructure.Projections;

/// <summary>
/// Phase P1 read-model row materialized by <c>JobStatusHistoryProjector</c>
/// from Planning integration events. Mirrors the same shape as the
/// DeliveryOrder counterpart so a single shared
/// <c>&lt;TimelineView /&gt;</c> component can render either.
///
/// FromStatus is null for the first row of a job — same rule as Order
/// (see docs/event-projection-plan.md decision log).
/// </summary>
public class JobStatusHistoryRow
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid JobId { get; private set; }
    public Guid DeliveryOrderId { get; private set; }
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = string.Empty;
    public DateTime OccurredAt { get; private set; }
    public string? Reason { get; private set; }

    private JobStatusHistoryRow() { }   // EF

    public JobStatusHistoryRow(
        Guid eventId, Guid jobId, Guid deliveryOrderId,
        string? fromStatus, string toStatus, DateTime occurredAt, string? reason)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("EventId is required.", nameof(eventId));
        if (jobId == Guid.Empty)
            throw new ArgumentException("JobId is required.", nameof(jobId));
        if (string.IsNullOrWhiteSpace(toStatus))
            throw new ArgumentException("ToStatus is required.", nameof(toStatus));

        Id = Guid.NewGuid();
        EventId = eventId;
        JobId = jobId;
        DeliveryOrderId = deliveryOrderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        OccurredAt = occurredAt;
        Reason = reason;
    }
}
