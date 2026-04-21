using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class DeliveryOrder : AggregateRoot<Guid>
{
    public string OrderKey { get; private set; } = string.Empty;
    public string PickupLocationCode { get; private set; } = string.Empty;
    public string DropLocationCode { get; private set; } = string.Empty;
    public OrderPriority Priority { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime? SLA { get; private set; }
    
    // Mapped later during validation
    public Guid? PickupStationId { get; private set; }
    public Guid? DropStationId { get; private set; }

    private readonly List<OrderLine> _orderLines = new();
    public IReadOnlyCollection<OrderLine> OrderLines => _orderLines.AsReadOnly();

    public RecurringSchedule? Schedule { get; private set; }

    private DeliveryOrder() { } // For EF Core

    public DeliveryOrder(string orderKey, string pickupLocationCode, string dropLocationCode, OrderPriority priority, DateTime? sla)
    {
        Id = Guid.NewGuid();
        OrderKey = orderKey;
        PickupLocationCode = pickupLocationCode;
        DropLocationCode = dropLocationCode;
        Priority = priority;
        SLA = sla;
        Status = OrderStatus.Submitted;

        AddDomainEvent(new DeliveryOrderSubmittedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, OrderKey));
    }

    public void AddOrderLine(string itemCode, double quantity, double weight, string? remarks = null)
    {
        var line = new OrderLine(Id, itemCode, quantity, weight, remarks);
        _orderLines.Add(line);
    }

    public void SetRecurringSchedule(string cronExpression, DateTime? validFrom, DateTime? validUntil)
    {
        Schedule = new RecurringSchedule(Id, cronExpression, validFrom, validUntil);
    }

    public void MarkAsValidated(Guid pickupStationId, Guid dropStationId)
    {
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Only submitted orders can be validated.");

        PickupStationId = pickupStationId;
        DropStationId = dropStationId;
        Status = OrderStatus.Validated;

        AddDomainEvent(new DeliveryOrderValidatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Completed || Status == OrderStatus.Executing)
            throw new InvalidOperationException("Cannot cancel an order that is executing or completed.");

        Status = OrderStatus.Cancelled;
        AddDomainEvent(new DeliveryOrderCancelledDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    public void MarkAsCompleted()
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Cannot complete a cancelled order.");

        Status = OrderStatus.Completed;
    }
}
