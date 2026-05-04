using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public enum AmendmentType { PriorityChange, LocationChange, SlaChange, CombinedChange, Cancel, Hold, Release, StatusTransition }

public class OrderAmendment : Entity<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public AmendmentType Type { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? OriginalSnapshot { get; private set; }
    public string? NewSnapshot { get; private set; }
    public string? AmendedBy { get; private set; }
    public DateTime AmendedAt { get; private set; }

    private OrderAmendment() { }

    public OrderAmendment(Guid orderId, AmendmentType type, string reason,
        string? originalSnapshot, string? newSnapshot, string? amendedBy = null)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = orderId;
        Type = type;
        Reason = reason;
        OriginalSnapshot = originalSnapshot;
        NewSnapshot = newSnapshot;
        AmendedBy = amendedBy;
        AmendedAt = DateTime.UtcNow;
    }
}
