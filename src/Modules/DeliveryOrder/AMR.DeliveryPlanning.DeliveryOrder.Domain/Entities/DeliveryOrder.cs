using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class DeliveryOrder : AggregateRoot<Guid>
{
    public Guid TenantId { get; private set; }
    public string OrderName { get; private set; } = string.Empty;
    public SlaTier SlaTier { get; private set; }
    public StructureType StructureType { get; private set; }
    public OrderStatus Status { get; private set; }
    public ServiceWindow ServiceWindow { get; private set; } = null!;
    public List<string> Tags { get; private set; } = [];

    private readonly List<DeliveryLeg> _legs = new();
    public IReadOnlyCollection<DeliveryLeg> Legs => _legs.AsReadOnly();

    public IReadOnlyCollection<PackageUnit> AllPackages =>
        _legs.SelectMany(l => l.Packages).ToList().AsReadOnly();

    public RecurringSchedule? Schedule { get; private set; }

    private DeliveryOrder() { }

    public static DeliveryOrder Create(Guid tenantId, string orderName, SlaTier slaTier,
        ServiceWindow serviceWindow, StructureType structureType = StructureType.Sequence,
        IEnumerable<string>? tags = null)
    {
        var order = new DeliveryOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderName = orderName,
            SlaTier = slaTier,
            ServiceWindow = serviceWindow,
            StructureType = structureType,
            Tags = tags?.ToList() ?? [],
            Status = OrderStatus.Draft
        };

        order.AddDomainEvent(new DeliveryOrderDraftedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, order.Id));
        return order;
    }

    public void Submit()
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Only Draft orders can be submitted.");

        Status = OrderStatus.Submitted;
        AddDomainEvent(new DeliveryOrderSubmittedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void AddPackage(
        string pickupLocationCode, string dropLocationCode,
        string carrierTypeCode,
        string barcode,
        string loadUnitProfileCode,
        double grossWeightKg,
        IEnumerable<(string itemNumber, double quantity)>? contents = null)
    {
        var leg = _legs.FirstOrDefault(l =>
            l.PickupLocationCode == pickupLocationCode &&
            l.DropLocationCode == dropLocationCode &&
            l.CarrierTypeCode == carrierTypeCode);

        if (leg is null)
        {
            leg = new DeliveryLeg(Id, _legs.Count + 1, pickupLocationCode, dropLocationCode, carrierTypeCode);
            _legs.Add(leg);
        }

        leg.AddPackage(barcode, loadUnitProfileCode, grossWeightKg, contents);
    }

    public void UpdateAllPackageStatuses(PackageStatus status)
    {
        foreach (var leg in _legs)
            leg.UpdateAllPackageStatuses(status);
    }

    public void MarkPackagesDelivered(IEnumerable<string> barcodes)
    {
        var barcodeSet = new HashSet<string>(barcodes, StringComparer.OrdinalIgnoreCase);
        foreach (var leg in _legs)
            foreach (var pkg in leg.Packages)
                if (barcodeSet.Contains(pkg.Barcode))
                    pkg.UpdateStatus(PackageStatus.Delivered);
    }

    public void SetRecurringSchedule(string cronExpression, DateTime? validFrom, DateTime? validUntil)
    {
        Schedule = new RecurringSchedule(Id, cronExpression, validFrom, validUntil);
    }

    public void AddTag(string tag)
    {
        if (!Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            Tags.Add(tag);
    }

    public void RemoveTag(string tag)
    {
        Tags.RemoveAll(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
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
            .Select(l => new DeliveryLegEventDto(
                l.Id,
                l.Sequence,
                l.PickupStationId!.Value,
                l.DropStationId!.Value,
                l.CarrierTypeCode,
                l.Packages.Count,
                l.Packages.Sum(p => p.GrossWeightKg),
                l.Packages.Select(p => new PackageSummaryEventDto(
                    p.Barcode, p.LoadUnitProfileCode, p.GrossWeightKg)).ToList()))
            .ToList();

        AddDomainEvent(new DeliveryOrderReadyToPlanDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, TenantId, Id, SlaTier.ToString(), legDtos));
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

    public void AmendServiceWindow(ServiceWindow newServiceWindow, string reason)
    {
        if (Status == OrderStatus.Completed || Status == OrderStatus.Cancelled)
            throw new InvalidOperationException($"Cannot amend a {Status} order.");

        ServiceWindow = newServiceWindow;
        Status = OrderStatus.Amended;
        AddDomainEvent(new DeliveryOrderAmendedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }
}
