using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class DeliveryOrder : AggregateRoot<Guid>
{
    public Guid TenantId { get; private set; }
    public int OrderId { get; private set; }
    public string OrderNo { get; private set; } = string.Empty;
    public string CreateBy { get; private set; } = string.Empty;
    public OrderPriority Priority { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime? SLA { get; private set; }

    private readonly List<DeliveryLeg> _legs = new();
    public IReadOnlyCollection<DeliveryLeg> Legs => _legs.AsReadOnly();

    public IReadOnlyCollection<OrderItem> AllOrderItems =>
        _legs.SelectMany(l => l.OrderItems).ToList().AsReadOnly();

    public RecurringSchedule? Schedule { get; private set; }

    private DeliveryOrder() { } // For EF Core

    public DeliveryOrder(Guid tenantId, int orderId, string orderNo, string createBy, OrderPriority priority, DateTime? sla)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        OrderId = orderId;
        OrderNo = orderNo;
        CreateBy = createBy;
        Priority = priority;
        SLA = sla;
        Status = OrderStatus.Submitted;

        AddDomainEvent(new DeliveryOrderSubmittedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, OrderNo));
    }

    public void AddOrderItem(string pickupLocationCode, string dropLocationCode,
        int workOrderId, string workOrder, int itemId, string itemNumber,
        string itemDescription, double quantity, double weight,
        string? line = null, string? model = null, string? remarks = null)
    {
        var leg = _legs.FirstOrDefault(l =>
            l.PickupLocationCode == pickupLocationCode &&
            l.DropLocationCode == dropLocationCode);

        if (leg is null)
        {
            leg = new DeliveryLeg(Id, _legs.Count + 1, pickupLocationCode, dropLocationCode);
            _legs.Add(leg);
        }

        leg.AddItem(workOrderId, workOrder, itemId, itemNumber, itemDescription, quantity, weight, line, model, remarks);
    }

    public void UpdateAllItemStatuses(Enums.OrderItemStatus status)
    {
        foreach (var leg in _legs)
            leg.UpdateAllItemStatuses(status);
    }

    public void SetRecurringSchedule(string cronExpression, DateTime? validFrom, DateTime? validUntil)
    {
        Schedule = new RecurringSchedule(Id, cronExpression, validFrom, validUntil);
    }

    public void MarkAsValidated(IReadOnlyDictionary<(string pickup, string drop), (Guid pickupStationId, Guid dropStationId)> stationMap)
    {
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Only submitted orders can be validated.");

        foreach (var leg in _legs)
        {
            if (!stationMap.TryGetValue((leg.PickupLocationCode, leg.DropLocationCode), out var stations))
                throw new InvalidOperationException($"Missing station mapping for leg {leg.PickupLocationCode} → {leg.DropLocationCode}.");
            leg.SetStationIds(stations.pickupStationId, stations.dropStationId);
        }

        Status = OrderStatus.Validated;
        AddDomainEvent(new DeliveryOrderValidatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void MarkReadyToPlan()
    {
        if (Status != OrderStatus.Validated)
            throw new InvalidOperationException("Only validated orders can be marked ready to plan.");

        Status = OrderStatus.ReadyToPlan;

        var legDtos = _legs
            .OrderBy(l => l.Sequence)
            .Select(l => new DeliveryLegEventDto(l.Id, l.Sequence, l.PickupStationId!.Value, l.DropStationId!.Value))
            .ToList();

        AddDomainEvent(new DeliveryOrderReadyToPlanDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, TenantId, Id, Priority.ToString(), legDtos));
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
        AddDomainEvent(new DeliveryOrderFailedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, TenantId, Id, reason));
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Completed || Status == OrderStatus.InProgress)
            throw new InvalidOperationException("Cannot cancel an order that is in progress or completed.");

        Status = OrderStatus.Cancelled;
        AddDomainEvent(new DeliveryOrderCancelledDomainEvent(Guid.NewGuid(), DateTime.UtcNow, TenantId, Id, reason));
    }

    public void MarkAsCompleted()
    {
        if (Status != OrderStatus.InProgress && Status != OrderStatus.Dispatched)
            throw new InvalidOperationException($"Cannot complete an order in {Status} status.");

        Status = OrderStatus.Completed;
        AddDomainEvent(new DeliveryOrderCompletedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, TenantId, Id));
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
