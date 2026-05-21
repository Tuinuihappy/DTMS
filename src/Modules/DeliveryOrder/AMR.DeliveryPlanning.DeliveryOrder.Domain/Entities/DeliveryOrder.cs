using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class DeliveryOrder : AggregateRoot<Guid>, IAuditable
{
    public string OrderRef { get; private set; } = string.Empty;
    public SourceSystem SourceSystem { get; private set; }
    public Priority Priority { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime? RequestedDeliveryDate { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime CreatedDate { get; private set; }
    public DateTime? UpdatedDate { get; private set; }
    public double TotalWeightKg { get; private set; }
    public double TotalQuantity { get; private set; }
    public int TotalItems { get; private set; }

    private readonly List<Item> _items = new();
    public IReadOnlyCollection<Item> Items => _items.AsReadOnly();

    private DeliveryOrder() { }

    void IAuditable.SetCreatedAt(DateTime createdAt) => CreatedDate = createdAt;
    void IAuditable.SetUpdatedAt(DateTime updatedAt) => UpdatedDate = updatedAt;

    public static DeliveryOrder Create(string orderRef, Priority priority,
        DateTime? requestedDeliveryDate, SourceSystem sourceSystem = SourceSystem.Manual,
        string? createdBy = null)
    {
        var order = new DeliveryOrder
        {
            Id = Guid.NewGuid(),
            OrderRef = orderRef,
            Priority = priority,
            RequestedDeliveryDate = requestedDeliveryDate,
            Status = OrderStatus.Draft,
            SourceSystem = sourceSystem,
            CreatedBy = createdBy
        };

        order.AddDomainEvent(new DeliveryOrderDraftedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, order.Id));
        return order;
    }

    public void UpdateDraft(string orderRef, Priority priority, DateTime? requestedDeliveryDate)
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException($"Only Draft orders can be edited. Current status: {Status}.");

        OrderRef = orderRef;
        Priority = priority;
        RequestedDeliveryDate = requestedDeliveryDate;

        _items.Clear();
        TotalWeightKg = 0;
        TotalQuantity = 0;
        TotalItems = 0;

        AddDomainEvent(new DeliveryOrderDraftUpdatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void Submit()
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Only Draft orders can be submitted.");

        Status = OrderStatus.Submitted;
        AddDomainEvent(new DeliveryOrderSubmittedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void AddItem(
        string pickupLocationCode, string dropLocationCode,
        int itemSeq, string sku, string? description, CargoType cargoType,
        string? loadUnitProfileCode,
        Dimensions? dimensions, double? weightKg, double quantity, string uom,
        CargoSpecific? cargoSpecific = null)
    {
        if (_items.Any(p => p.ItemSeq == itemSeq))
            throw new InvalidOperationException($"An item with seq '{itemSeq}' already exists in this order.");

        _items.Add(new Item(Id, pickupLocationCode, dropLocationCode, itemSeq, sku, description, cargoType, loadUnitProfileCode, dimensions, weightKg, quantity, uom, cargoSpecific));
        TotalWeightKg += weightKg ?? 0;
        TotalQuantity += quantity;
        TotalItems++;
    }

    public void UpdateAllItemStatuses(ItemStatus status)
    {
        foreach (var item in _items)
            item.UpdateStatus(status);
    }

    public void MarkItemsDelivered(IEnumerable<string> skus)
    {
        var skuSet = new HashSet<string>(skus, StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items)
            if (skuSet.Contains(item.Sku))
                item.UpdateStatus(ItemStatus.Delivered);
    }

    public void MarkAsValidated(IReadOnlyDictionary<(string pickup, string drop), (Guid pickupStationId, Guid dropStationId)> stationMap)
    {
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Only submitted orders can be validated.");

        foreach (var item in _items)
        {
            if (!stationMap.TryGetValue((item.PickupLocationCode, item.DropLocationCode), out var stations))
                throw new InvalidOperationException($"Missing station mapping for {item.PickupLocationCode} → {item.DropLocationCode}.");
            item.SetStationIds(stations.pickupStationId, stations.dropStationId);
        }

        Status = OrderStatus.Validated;
        AddDomainEvent(new DeliveryOrderValidatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void MarkReadyToPlan()
    {
        if (Status != OrderStatus.Validated)
            throw new InvalidOperationException("Only validated orders can be marked ready to plan.");

        Status = OrderStatus.ReadyToPlan;

        var itemDtos = _items
            .Select(p => new ItemEventDto(
                p.Sku, p.WeightKg ?? 0,
                p.PickupStationId!.Value, p.DropStationId!.Value))
            .ToList();

        AddDomainEvent(new DeliveryOrderReadyToPlanDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, Priority.ToString(),
            RequestedDeliveryDate, itemDtos));
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
        if (Status == OrderStatus.Cancelled) return;
        if (Status is OrderStatus.Completed or OrderStatus.InProgress)
            throw new InvalidOperationException("Cannot cancel an order that is in progress or completed.");

        Status = OrderStatus.Cancelled;
        AddDomainEvent(new DeliveryOrderCancelledDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    public void MarkAsCompleted()
    {
        if (Status != OrderStatus.InProgress && Status != OrderStatus.Dispatched)
            throw new InvalidOperationException($"Cannot complete an order in {Status} status.");

        Status = OrderStatus.Completed;
        AddDomainEvent(new DeliveryOrderCompletedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void AmendRequestedDeliveryDate(DateTime? newRequestedDeliveryDate, string reason)
    {
        if (Status is OrderStatus.Draft)
            throw new InvalidOperationException("Cannot amend a Draft order — use UpdateDraft instead.");

        if (Status is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new InvalidOperationException($"Cannot amend a {Status} order.");

        RequestedDeliveryDate = newRequestedDeliveryDate;
        AddDomainEvent(new DeliveryOrderAmendedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }
}
