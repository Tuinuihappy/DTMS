using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Domain;

namespace DTMS.DeliveryOrder.Domain.Entities;

/// <summary>
/// One operator POD scan against an Item. An Item has at most one
/// ItemPodEvent per ScanType — the (ItemId, ScanType) pair is unique in
/// the schema, so a second scan against the same checkpoint is rejected
/// by the database. Idempotency at the application layer is enforced via
/// the aggregate (DeliveryOrder.RecordItemPod is a no-op if the event
/// already exists).
///
/// Created and owned by Item. Not its own aggregate root — modifications
/// go through DeliveryOrder.RecordItemPod and persist on the order's
/// SaveChangesAsync.
/// </summary>
public class ItemPodEvent : Entity<Guid>
{
    public Guid ItemId { get; private set; }
    public PodScanType ScanType { get; private set; }
    public DateTime ScannedAt { get; private set; }
    public string ScannedBy { get; private set; } = string.Empty;
    public string Method { get; private set; } = string.Empty;   // "Barcode" / "Manual" / "Signature" / "Confirm"
    public string? Reference { get; private set; }               // scanned code / signature hash / null

    private ItemPodEvent() { }

    internal ItemPodEvent(Guid itemId, PodScanType scanType, string scannedBy, string method, string? reference)
    {
        if (string.IsNullOrWhiteSpace(scannedBy))
            throw new ArgumentException("ScannedBy must not be empty.", nameof(scannedBy));
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("Method must not be empty.", nameof(method));

        Id = Guid.NewGuid();
        ItemId = itemId;
        ScanType = scanType;
        ScannedAt = DateTime.UtcNow;
        ScannedBy = scannedBy;
        Method = method;
        Reference = reference;
    }
}
