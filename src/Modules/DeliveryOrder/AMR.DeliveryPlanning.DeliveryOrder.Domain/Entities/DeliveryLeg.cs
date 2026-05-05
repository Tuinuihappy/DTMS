using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class DeliveryLeg : Entity<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public int Sequence { get; private set; }
    public string PickupLocationCode { get; private set; } = string.Empty;
    public string DropLocationCode { get; private set; } = string.Empty;
    public Guid? PickupStationId { get; private set; }
    public Guid? DropStationId { get; private set; }

    private readonly List<OrderItem> _orderItems = new();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private DeliveryLeg() { } // For EF Core

    internal DeliveryLeg(Guid deliveryOrderId, int sequence, string pickupLocationCode, string dropLocationCode)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = deliveryOrderId;
        Sequence = sequence;
        PickupLocationCode = pickupLocationCode;
        DropLocationCode = dropLocationCode;
    }

    internal void AddItem(int workOrderId, string workOrder, int itemId, string itemNumber,
        string itemDescription, double quantity, double weight,
        string? line = null, string? model = null, string? remarks = null)
    {
        _orderItems.Add(new OrderItem(Id, workOrderId, workOrder, itemId, itemNumber, itemDescription, quantity, weight, line, model, remarks));
    }

    internal void UpdateAllItemStatuses(OrderItemStatus status)
    {
        foreach (var item in _orderItems)
            item.UpdateStatus(status);
    }

    internal void SetStationIds(Guid pickupStationId, Guid dropStationId)
    {
        PickupStationId = pickupStationId;
        DropStationId = dropStationId;
    }
}
