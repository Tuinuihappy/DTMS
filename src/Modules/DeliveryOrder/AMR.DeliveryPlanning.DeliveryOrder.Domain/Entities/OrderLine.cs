using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class OrderLine : Entity<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public string ItemCode { get; private set; }
    public double Quantity { get; private set; }
    public double Weight { get; private set; }
    public string? Remarks { get; private set; }

    private OrderLine() { ItemCode = null!; } // For EF Core

    internal OrderLine(Guid deliveryOrderId, string itemCode, double quantity, double weight, string? remarks)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = deliveryOrderId;
        ItemCode = itemCode;
        Quantity = quantity;
        Weight = weight;
        Remarks = remarks;
    }
}
