using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class PackageContent : Entity<Guid>
{
    public Guid PackageUnitId { get; private set; }
    public string ItemNumber { get; private set; } = string.Empty;
    public double Quantity { get; private set; }

    private PackageContent() { }

    internal PackageContent(Guid packageUnitId, string itemNumber, double quantity)
    {
        Id = Guid.NewGuid();
        PackageUnitId = packageUnitId;
        ItemNumber = itemNumber;
        Quantity = quantity;
    }
}

public class PackageUnit : Entity<Guid>
{
    public Guid DeliveryLegId { get; private set; }
    public string Barcode { get; private set; } = string.Empty;
    public string LoadUnitProfileCode { get; private set; } = string.Empty;
    public double GrossWeightKg { get; private set; }
    public PackageStatus Status { get; private set; }

    private readonly List<PackageContent> _contents = new();
    public IReadOnlyCollection<PackageContent> Contents => _contents.AsReadOnly();

    private PackageUnit() { }

    internal PackageUnit(Guid deliveryLegId, string barcode, string loadUnitProfileCode,
        double grossWeightKg, IEnumerable<(string itemNumber, double quantity)>? contents = null)
    {
        Id = Guid.NewGuid();
        DeliveryLegId = deliveryLegId;
        Barcode = barcode;
        LoadUnitProfileCode = loadUnitProfileCode;
        GrossWeightKg = grossWeightKg;
        Status = PackageStatus.Pending;

        foreach (var (itemNumber, qty) in contents ?? [])
            _contents.Add(new PackageContent(Id, itemNumber, qty));
    }

    internal void UpdateStatus(PackageStatus status) => Status = status;
}
