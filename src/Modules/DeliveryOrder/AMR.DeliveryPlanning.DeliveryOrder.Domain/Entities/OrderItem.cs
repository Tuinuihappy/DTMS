using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class OrderItem : Entity<Guid>
{
    public Guid DeliveryLegId { get; private set; }
    public int WorkOrderId { get; private set; }
    public string WorkOrder { get; private set; } = string.Empty;
    public int ItemId { get; private set; }
    public string ItemNumber { get; private set; } = string.Empty;
    public string ItemDescription { get; private set; } = string.Empty;
    public double Quantity { get; private set; }
    public double Weight { get; private set; }
    public string? Line { get; private set; }
    public string? Model { get; private set; }
    public string? Remarks { get; private set; }
    public OrderItemStatus ItemStatus { get; private set; }

    private OrderItem() { } // For EF Core

    internal OrderItem(Guid deliveryLegId, int workOrderId, string workOrder, int itemId,
        string itemNumber, string itemDescription, double quantity, double weight,
        string? line, string? model, string? remarks)
    {
        Id = Guid.NewGuid();
        DeliveryLegId = deliveryLegId;
        WorkOrderId = workOrderId;
        WorkOrder = workOrder;
        ItemId = itemId;
        ItemNumber = itemNumber;
        ItemDescription = itemDescription;
        Quantity = quantity;
        Weight = weight;
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
