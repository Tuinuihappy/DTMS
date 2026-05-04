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

    private readonly List<OrderLine> _orderLines = new();
    public IReadOnlyCollection<OrderLine> OrderLines => _orderLines.AsReadOnly();

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
        string itemDescription, double quantity, double weight, string? remarks = null)
    {
        _orderLines.Add(new OrderLine(Id, workOrderId, workOrder, itemId, itemNumber, itemDescription, quantity, weight, remarks));
    }

    internal void UpdateAllItemStatuses(OrderLineStatus status)
    {
        foreach (var line in _orderLines)
            line.UpdateStatus(status);
    }

    internal void SetStationIds(Guid pickupStationId, Guid dropStationId)
    {
        PickupStationId = pickupStationId;
        DropStationId = dropStationId;
    }
}
