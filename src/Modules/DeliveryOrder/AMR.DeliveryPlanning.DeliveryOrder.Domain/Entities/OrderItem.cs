using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class OrderItem : Entity<Guid>
{
    public Guid DeliveryLegId { get; private set; }
    public string? WorkOrder { get; private set; }
    public string ItemNumber { get; private set; } = string.Empty;
    public string ItemDescription { get; private set; } = string.Empty;
    public double Quantity { get; private set; }
    public double? Weight { get; private set; }
    public LoadUnitType LoadUnitType { get; private set; }
    public Dims? Dims { get; private set; }
    public int? HazmatClass { get; private set; }
    public TemperatureRange? TemperatureRange { get; private set; }
    public List<HandlingInstruction> HandlingInstructions { get; private set; } = [];
    public string? Line { get; private set; }
    public string? Model { get; private set; }
    public string? Remarks { get; private set; }
    public OrderItemStatus ItemStatus { get; private set; }

    private OrderItem() { } // For EF Core

    internal OrderItem(Guid deliveryLegId, string? workOrder, string itemNumber,
        string itemDescription, double quantity, double? weight,
        LoadUnitType loadUnitType,
        string? line = null, string? model = null, string? remarks = null,
        Dims? dims = null, int? hazmatClass = null,
        TemperatureRange? temperatureRange = null,
        IEnumerable<HandlingInstruction>? handlingInstructions = null)
    {
        Id = Guid.NewGuid();
        DeliveryLegId = deliveryLegId;
        WorkOrder = workOrder;
        ItemNumber = itemNumber;
        ItemDescription = itemDescription;
        Quantity = quantity;
        Weight = weight;
        LoadUnitType = loadUnitType;
        Dims = dims;
        HazmatClass = hazmatClass;
        TemperatureRange = temperatureRange;
        HandlingInstructions = handlingInstructions?.ToList() ?? [];
        Line = line;
        Model = model;
        Remarks = remarks;
        ItemStatus = OrderItemStatus.Pending;
    }

    internal void UpdateStatus(OrderItemStatus status)
    {
        ItemStatus = status;
    }
}
