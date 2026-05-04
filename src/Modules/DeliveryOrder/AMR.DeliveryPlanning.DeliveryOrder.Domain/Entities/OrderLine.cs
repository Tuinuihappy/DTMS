using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class OrderLine : Entity<Guid>
{
    public Guid DeliveryLegId { get; private set; }
    public int WorkOrderId { get; private set; }
    public string WorkOrder { get; private set; } = string.Empty;
    public int ItemId { get; private set; }
    public string ItemNumber { get; private set; } = string.Empty;
    public string ItemDescription { get; private set; } = string.Empty;
    public double Quantity { get; private set; }
    public double Weight { get; private set; }
    public string? Remarks { get; private set; }
    public OrderLineStatus ItemStatus { get; private set; }

    private OrderLine() { } // For EF Core

    internal OrderLine(Guid deliveryLegId, int workOrderId, string workOrder, int itemId,
        string itemNumber, string itemDescription, double quantity, double weight, string? remarks)
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
        Remarks = remarks;
        ItemStatus = OrderLineStatus.Pending;
    }

    internal void UpdateStatus(OrderLineStatus status)
    {
        ItemStatus = status;
    }
}
