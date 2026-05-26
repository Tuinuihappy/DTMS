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
    public string? LoadUnitProfileCode { get; private set; }
    public Dimensions? Dimensions { get; private set; }
    public double? WeightKg { get; private set; }
    public Quantity Quantity { get; private set; } = null!;
    public CargoType? CargoType { get; private set; }
    public CargoSpecific? CargoSpecific { get; private set; }
    public HazmatInfo? Hazmat { get; private set; }
    public TemperatureRange? Temperature { get; private set; }
    public ItemStatus Status { get; private set; }

    private Item() { }

    internal Item(Guid deliveryOrderId, string pickupLocationCode, string dropLocationCode,
        int itemSeq, string sku, string? description,
        string? loadUnitProfileCode,
        Dimensions? dimensions, double? weightKg, Quantity quantity,
        CargoType? cargoType,
        CargoSpecific? cargoSpecific = null,
        HazmatInfo? hazmat = null,
        TemperatureRange? temperature = null)
    {
        if (cargoType is null && cargoSpecific is not null)
            throw new InvalidOperationException(
                "CargoSpecific must be null when CargoType is not specified.");

        if (string.IsNullOrWhiteSpace(pickupLocationCode))
            throw new ArgumentException("PickupLocationCode must not be empty.", nameof(pickupLocationCode));
        if (string.IsNullOrWhiteSpace(dropLocationCode))
            throw new ArgumentException("DropLocationCode must not be empty.", nameof(dropLocationCode));
        ArgumentNullException.ThrowIfNull(quantity);

        Id = Guid.NewGuid();
        DeliveryOrderId = deliveryOrderId;
        PickupLocationCode = pickupLocationCode.Trim();
        DropLocationCode = dropLocationCode.Trim();
        ItemSeq = itemSeq;
        Sku = sku;
        Description = description;
        LoadUnitProfileCode = loadUnitProfileCode;
        Dimensions = dimensions;
        WeightKg = weightKg;
        Quantity = quantity;
        CargoType = cargoType;
        CargoSpecific = cargoSpecific;
        Hazmat = hazmat;
        Temperature = temperature;
        Status = ItemStatus.Pending;
    }

    internal void SetStationIds(Guid pickupStationId, Guid dropStationId)
    {
        PickupStationId = pickupStationId;
        DropStationId = dropStationId;
    }

    internal void UpdateStatus(ItemStatus status) => Status = status;
}
