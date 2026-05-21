using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class Item : Entity<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public string PickupLocationCode { get; private set; } = string.Empty;
    public string DropLocationCode { get; private set; } = string.Empty;
    public Guid? PickupStationId { get; private set; }
    public Guid? DropStationId { get; private set; }
    public int ItemSeq { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public CargoType CargoType { get; private set; }
    public string? LoadUnitProfileCode { get; private set; }
    public Dimensions? Dimensions { get; private set; }
    public double? WeightKg { get; private set; }
    public double Quantity { get; private set; }
    public string Uom { get; private set; } = string.Empty;
    public CargoSpecific? CargoSpecific { get; private set; }
    public ItemStatus Status { get; private set; }

    private Item() { }

    internal Item(Guid deliveryOrderId, string pickupLocationCode, string dropLocationCode,
        int itemSeq, string sku, string? description, CargoType cargoType,
        string? loadUnitProfileCode,
        Dimensions? dimensions, double? weightKg, double quantity, string uom,
        CargoSpecific? cargoSpecific = null)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = deliveryOrderId;
        PickupLocationCode = pickupLocationCode;
        DropLocationCode = dropLocationCode;
        ItemSeq = itemSeq;
        Sku = sku;
        Description = description;
        CargoType = cargoType;
        LoadUnitProfileCode = loadUnitProfileCode;
        Dimensions = dimensions;
        WeightKg = weightKg;
        Quantity = quantity;
        Uom = uom;
        CargoSpecific = cargoSpecific;
        Status = ItemStatus.Pending;
    }

    internal void SetStationIds(Guid pickupStationId, Guid dropStationId)
    {
        PickupStationId = pickupStationId;
        DropStationId = dropStationId;
    }

    internal void UpdateStatus(ItemStatus status) => Status = status;
}
