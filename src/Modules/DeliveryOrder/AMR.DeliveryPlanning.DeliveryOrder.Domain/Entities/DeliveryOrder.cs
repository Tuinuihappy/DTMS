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
    public SlaTier SlaTier { get; private set; } = SlaTier.Bronze;
    public OrderStatus Status { get; private set; }
    public ServiceWindow? ServiceWindow { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
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
        ServiceWindow? serviceWindow, SourceSystem sourceSystem = SourceSystem.Manual,
        string? createdBy = null, SlaTier slaTier = SlaTier.Bronze)
    {
        var order = new DeliveryOrder
        {
            Id = Guid.NewGuid(),
            OrderRef = orderRef,
            Priority = priority,
            SlaTier = slaTier,
            ServiceWindow = serviceWindow,
            Status = OrderStatus.Draft,
            SourceSystem = sourceSystem,
            CreatedBy = createdBy
        };

        order.AddDomainEvent(new DeliveryOrderDraftedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, order.Id));
        return order;
    }

    public static DeliveryOrder CreateFromUpstream(string orderRef, Priority priority,
        ServiceWindow? serviceWindow, SourceSystem sourceSystem, string? createdBy = null,
        SlaTier slaTier = SlaTier.Bronze)
    {
        if (sourceSystem == SourceSystem.Manual)
            throw new InvalidOperationException("Upstream orders cannot have Manual source system.");

        var order = new DeliveryOrder
        {
            Id = Guid.NewGuid(),
            OrderRef = orderRef,
            Priority = priority,
            SlaTier = slaTier,
            ServiceWindow = serviceWindow,
            Status = OrderStatus.Submitted,
            SubmittedAt = DateTime.UtcNow,
            SourceSystem = sourceSystem,
            CreatedBy = createdBy
        };

        order.AddDomainEvent(new DeliveryOrderSubmittedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, order.Id));
        return order;
    }

    public void UpdateDraft(string orderRef, Priority priority, ServiceWindow? serviceWindow,
        SlaTier slaTier = SlaTier.Bronze)
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException($"Only Draft orders can be edited. Current status: {Status}.");

        OrderRef = orderRef;
        Priority = priority;
        SlaTier = slaTier;
        ServiceWindow = serviceWindow;

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
        // SLA clock starts at first submit. Subsequent transitions (e.g., release back to Confirmed
        // after a Hold) do not re-route through Submit(), so this assignment runs exactly once.
        SubmittedAt ??= DateTime.UtcNow;
        AddDomainEvent(new DeliveryOrderSubmittedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void AddItem(
        string pickupLocationCode, string dropLocationCode,
        int itemSeq, string sku, string? description,
        string? loadUnitProfileCode,
        Dimensions? dimensions, double? weightKg, Quantity quantity,
        CargoType? cargoType,
        CargoSpecific? cargoSpecific = null,
        HazmatInfo? hazmat = null,
        TemperatureRange? temperature = null)
    {
        if (_items.Any(p => p.ItemSeq == itemSeq))
            throw new InvalidOperationException($"An item with seq '{itemSeq}' already exists in this order.");

        _items.Add(new Item(Id, pickupLocationCode, dropLocationCode, itemSeq, sku, description, loadUnitProfileCode, dimensions, weightKg, quantity, cargoType, cargoSpecific, hazmat, temperature));
        TotalWeightKg += weightKg ?? 0;
        TotalQuantity += quantity.Value;
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

    public void MarkAsValidated(IReadOnlyDictionary<string, Guid> stationMap)
    {
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Only submitted orders can be validated.");

        foreach (var item in _items)
        {
            if (!stationMap.TryGetValue(item.PickupLocationCode, out var pickupId))
                throw new InvalidOperationException($"Missing station mapping for pickup {item.PickupLocationCode}.");
            if (!stationMap.TryGetValue(item.DropLocationCode, out var dropId))
                throw new InvalidOperationException($"Missing station mapping for drop {item.DropLocationCode}.");
            item.SetStationIds(pickupId, dropId);
        }

        Status = OrderStatus.Validated;
        AddDomainEvent(new DeliveryOrderValidatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void Confirm(double weightFallbackKg)
    {
        if (Status != OrderStatus.Validated)
            throw new InvalidOperationException($"Only Validated orders can be confirmed. Current status: {Status}.");

        Status = OrderStatus.Confirmed;
        AddDomainEvent(BuildConfirmedEvent(weightFallbackKg));
    }

    public void Reject(string reason)
    {
        if (Status is not (OrderStatus.Submitted or OrderStatus.Validated or OrderStatus.Confirmed))
            throw new InvalidOperationException($"Cannot reject an order in {Status} status.");

        Status = OrderStatus.Rejected;
        AddDomainEvent(new DeliveryOrderRejectedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    public void MarkPlanning()
    {
        if (Status != OrderStatus.Confirmed)
            throw new InvalidOperationException("Only Confirmed orders can enter Planning.");

        Status = OrderStatus.Planning;
        AddDomainEvent(new DeliveryOrderPlanningStartedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    private DeliveryOrderConfirmedDomainEvent BuildConfirmedEvent(double weightFallbackKg)
    {
        var itemDtos = _items
            .Select(p => new ItemEventDto(
                p.Sku, p.WeightKg ?? weightFallbackKg,
                p.PickupStationId!.Value, p.DropStationId!.Value,
                p.Hazmat is { } hz ? new ItemHazmatDto(hz.ClassCode, hz.PackingGroup?.ToString()) : null,
                p.Temperature is { } tr ? new ItemTemperatureDto(tr.MinC, tr.MaxC) : null))
            .ToList();

        return new DeliveryOrderConfirmedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, Priority.ToString(), SlaTier.ToString(),
            ServiceWindow?.Earliest, ServiceWindow?.Latest,
            Deadline: ServiceWindow?.Latest,
            SubmittedAt, itemDtos);
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

    public void Release(double weightFallbackKg)
    {
        if (Status != OrderStatus.Held)
            throw new InvalidOperationException("Only held orders can be released.");

        Status = OrderStatus.Confirmed;
        AddDomainEvent(new DeliveryOrderReleasedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
        AddDomainEvent(BuildConfirmedEvent(weightFallbackKg));
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

    public void AmendServiceWindow(ServiceWindow? newServiceWindow, string reason)
    {
        if (Status is OrderStatus.Draft)
            throw new InvalidOperationException("Cannot amend a Draft order — use UpdateDraft instead.");

        if (Status is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new InvalidOperationException($"Cannot amend a {Status} order.");

        ServiceWindow = newServiceWindow;
        AddDomainEvent(new DeliveryOrderAmendedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }
}
