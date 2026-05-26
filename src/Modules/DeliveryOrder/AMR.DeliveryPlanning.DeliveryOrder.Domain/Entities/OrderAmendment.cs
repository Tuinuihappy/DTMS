using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

// ServiceWindowChange is the only type in active use.
// PriorityChange, LocationChange, CombinedChange, Cancel, Hold, Release, StatusTransition
// are reserved for future amendment types when the full re-plan flow is implemented.
public enum AmendmentType { PriorityChange, LocationChange, ServiceWindowChange, CombinedChange, Cancel, Hold, Release, StatusTransition }

public class OrderAmendment : Entity<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public AmendmentType Type { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? OriginalSnapshot { get; private set; }
    public string? NewSnapshot { get; private set; }
    public string? AmendedBy { get; private set; }
    public DateTime AmendedAt { get; private set; }

    /// <summary>
    /// Schema version of the JSON in <see cref="OriginalSnapshot"/> /
    /// <see cref="NewSnapshot"/>. 0 = legacy narrow shape (only ServiceWindow +
    /// OrderStatus), 1 = full-order snapshot (P1-10). New columns must bump
    /// this so readers know which deserializer to use.
    /// </summary>
    public int AmendmentVersion { get; private set; }

    private OrderAmendment() { }

    public OrderAmendment(Guid orderId, AmendmentType type, string reason,
        string? originalSnapshot, string? newSnapshot, string? amendedBy = null,
        int amendmentVersion = 1)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = orderId;
        Type = type;
        Reason = reason;
        OriginalSnapshot = originalSnapshot;
        NewSnapshot = newSnapshot;
        AmendedBy = amendedBy;
        AmendedAt = DateTime.UtcNow;
        AmendmentVersion = amendmentVersion;
    }
}
