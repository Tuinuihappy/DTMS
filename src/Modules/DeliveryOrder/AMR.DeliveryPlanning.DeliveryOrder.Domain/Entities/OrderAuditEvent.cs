using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class OrderAuditEvent : Entity<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string? Details { get; private set; }
    public string? ActorId { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private OrderAuditEvent() { }

    public OrderAuditEvent(Guid orderId, string eventType, string? details = null, string? actorId = null)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = orderId;
        EventType = eventType;
        Details = details;
        ActorId = actorId;
        OccurredAt = DateTime.UtcNow;
    }
}
