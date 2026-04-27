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

    public void MarkReadyToPlan()
    {
        if (Status != OrderStatus.Validated)
            throw new InvalidOperationException("Only validated orders can be marked ready to plan.");

        Status = OrderStatus.ReadyToPlan;
        AddDomainEvent(new DeliveryOrderReadyToPlanDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void MarkPlanning()
    {
        if (Status != OrderStatus.ReadyToPlan)
            throw new InvalidOperationException("Only ReadyToPlan orders can enter Planning.");

        Status = OrderStatus.Planning;
        AddDomainEvent(new DeliveryOrderPlanningStartedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void MarkPlanned()
    {
        if (Status != OrderStatus.Planning)
            throw new InvalidOperationException("Only Planning orders can be marked Planned.");

        Status = OrderStatus.Planned;
        AddDomainEvent(new DeliveryOrderPlannedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void MarkDispatched()
    {
        if (Status != OrderStatus.Planned)
            throw new InvalidOperationException("Only Planned orders can be dispatched.");

        Status = OrderStatus.Dispatched;
        AddDomainEvent(new DeliveryOrderDispatchedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void MarkInProgress()
    {
        if (Status != OrderStatus.Dispatched)
            throw new InvalidOperationException("Only Dispatched orders can be in progress.");

        Status = OrderStatus.InProgress;
        AddDomainEvent(new DeliveryOrderInProgressDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void Hold(string reason)
    {
        if (Status == OrderStatus.Completed || Status == OrderStatus.Cancelled || Status == OrderStatus.Failed)
            throw new InvalidOperationException($"Cannot hold an order in {Status} status.");

        Status = OrderStatus.Held;
        AddDomainEvent(new DeliveryOrderHeldDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    public void Release()
    {
        if (Status != OrderStatus.Held)
            throw new InvalidOperationException("Only held orders can be released.");

        Status = OrderStatus.ReadyToPlan;
        AddDomainEvent(new DeliveryOrderReleasedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void MarkFailed(string reason)
    {
        if (Status == OrderStatus.Completed || Status == OrderStatus.Cancelled)
            throw new InvalidOperationException($"Cannot fail an order in {Status} status.");

        Status = OrderStatus.Failed;
        AddDomainEvent(new DeliveryOrderFailedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Completed || Status == OrderStatus.InProgress)
            throw new InvalidOperationException("Cannot cancel an order that is in progress or completed.");

        Status = OrderStatus.Cancelled;
        AddDomainEvent(new DeliveryOrderCancelledDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    public void MarkAsCompleted()
    {
        if (Status != OrderStatus.InProgress && Status != OrderStatus.Dispatched)
            throw new InvalidOperationException($"Cannot complete an order in {Status} status.");

        Status = OrderStatus.Completed;
    }

    public void AmendPriority(OrderPriority newPriority, string reason)
    {
        if (Status == OrderStatus.Completed || Status == OrderStatus.Cancelled)
            throw new InvalidOperationException($"Cannot amend a {Status} order.");

        Priority = newPriority;
        Status = OrderStatus.Amended;
        AddDomainEvent(new DeliveryOrderAmendedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    public void AmendSla(DateTime newSla, string reason)
    {
        if (Status == OrderStatus.Completed || Status == OrderStatus.Cancelled)
            throw new InvalidOperationException($"Cannot amend a {Status} order.");

        SLA = newSla;
        Status = OrderStatus.Amended;
        AddDomainEvent(new DeliveryOrderAmendedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }
}
