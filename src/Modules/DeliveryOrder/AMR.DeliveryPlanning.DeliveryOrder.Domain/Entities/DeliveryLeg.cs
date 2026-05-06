using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class DeliveryLeg : Entity<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public int Sequence { get; private set; }
    public string PickupLocationCode { get; private set; } = string.Empty;
    public string DropLocationCode { get; private set; } = string.Empty;
    public string CarrierTypeCode { get; private set; } = string.Empty;
    public Guid? PickupStationId { get; private set; }
    public Guid? DropStationId { get; private set; }

    private readonly List<PackageUnit> _packages = new();
    public IReadOnlyCollection<PackageUnit> Packages => _packages.AsReadOnly();

    private DeliveryLeg() { }

    internal DeliveryLeg(Guid deliveryOrderId, int sequence,
        string pickupLocationCode, string dropLocationCode, string carrierTypeCode)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = deliveryOrderId;
        Sequence = sequence;
        PickupLocationCode = pickupLocationCode;
        DropLocationCode = dropLocationCode;
        CarrierTypeCode = carrierTypeCode;
    }

    internal void AddPackage(string barcode, string loadUnitProfileCode,
        double grossWeightKg,
        IEnumerable<(string itemNumber, double quantity)>? contents = null)
    {
        _packages.Add(new PackageUnit(Id, barcode, loadUnitProfileCode, grossWeightKg, contents));
    }

    internal void UpdateAllPackageStatuses(PackageStatus status)
    {
        foreach (var pkg in _packages)
            pkg.UpdateStatus(status);
    }

    internal void SetStationIds(Guid pickupStationId, Guid dropStationId)
    {
        PickupStationId = pickupStationId;
        DropStationId = dropStationId;
    }
}
