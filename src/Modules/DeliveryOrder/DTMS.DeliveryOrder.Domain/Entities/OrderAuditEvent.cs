using DTMS.SharedKernel.Domain;

namespace DTMS.DeliveryOrder.Domain.Entities;

public class OrderAuditEvent : Entity<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string? Details { get; private set; }
    public string? ActorId { get; private set; }
    public DateTime OccurredAt { get; private set; }

    // Phase S.1 follow-up — channel + display name lifted from the
    // ambient ActorContext at write time so the audit drawer can show
    // "ManualWeb · Titichai Poojaratkoon" instead of bare "86347852".
    // Nullable so old call sites (and consumer-side notify rows that
    // pre-date S.1) stay valid; new sites opt in via the extended ctor.
    public string? Channel { get; private set; }
    public string? DisplayName { get; private set; }

    // Phase C (multi-source) — which external system an upstream-callback
    // row concerns ('oms', 'sap', …). Replaces the system being baked into
    // the EventType string (UpstreamOmsNotified → UpstreamNotified +
    // SystemKey='oms') so onboarding a system never mints new event types.
    // Null on rows that aren't about an external system.
    public string? SystemKey { get; private set; }

    private OrderAuditEvent() { }

    public OrderAuditEvent(
        Guid orderId,
        string eventType,
        string? details = null,
        string? actorId = null,
        string? channel = null,
        string? displayName = null,
        string? systemKey = null)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = orderId;
        EventType = eventType;
        Details = details;
        ActorId = actorId;
        Channel = channel;
        DisplayName = displayName;
        SystemKey = systemKey;
        OccurredAt = DateTime.UtcNow;
    }
}
