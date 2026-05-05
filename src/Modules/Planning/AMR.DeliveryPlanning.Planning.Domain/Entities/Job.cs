using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Entities;

public class Job : AggregateRoot<Guid>
{
    public Guid TenantId { get; private set; }
    public Guid DeliveryOrderId { get; private set; }
    public JobStatus Status { get; private set; }
    public Guid? AssignedVehicleId { get; private set; }
    public string Priority { get; private set; } = "Normal";
    public double EstimatedDuration { get; private set; }
    public double EstimatedDistance { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Phase 2: Pattern + Multi-order support
    public PatternType Pattern { get; private set; } = PatternType.PointToPoint;
    public string? RequiredCapability { get; private set; }
    public double TotalWeight { get; private set; }

    // Phase 4+: Explainability + Predictive replanning
    public string? PlanningTrace { get; private set; }
    public DateTime? SlaDeadline { get; private set; }

    private readonly List<Guid> _derivedFromOrders = new();
    public IReadOnlyCollection<Guid> DerivedFromOrders => _derivedFromOrders.AsReadOnly();

    private readonly List<Leg> _legs = new();
    public IReadOnlyCollection<Leg> Legs => _legs.AsReadOnly();

    private Job() { } // EF Core

    public Job(Guid tenantId, Guid deliveryOrderId, string priority)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        DeliveryOrderId = deliveryOrderId;
        Priority = priority;
        Status = JobStatus.Created;
        CreatedAt = DateTime.UtcNow;
        _derivedFromOrders.Add(deliveryOrderId);

        AddDomainEvent(new JobCreatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId));
    }

    /// <summary>
    /// Create a Job from multiple consolidated orders.
    /// </summary>
    public Job(Guid tenantId, List<Guid> orderIds, string priority, PatternType pattern)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        DeliveryOrderId = orderIds.First();
        Priority = priority;
        Pattern = pattern;
        Status = JobStatus.Created;
        CreatedAt = DateTime.UtcNow;
        _derivedFromOrders.AddRange(orderIds);

        AddDomainEvent(new JobCreatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId));
    }

    public void SetPattern(PatternType pattern) => Pattern = pattern;
    public void SetRequiredCapability(string capability) => RequiredCapability = capability;
    public void SetTotalWeight(double weight) => TotalWeight = weight;
    public void SetSlaDeadline(DateTime deadline) => SlaDeadline = deadline;
    public void SetPlanningTrace(string trace) => PlanningTrace = trace;

    public bool IsSlaAtRisk(TimeSpan estimatedRemainingTime)
        => SlaDeadline.HasValue && SlaDeadline.Value - DateTime.UtcNow <= estimatedRemainingTime;

    public Leg AddLeg(Guid fromStationId, Guid toStationId, int sequenceOrder, double estimatedCost)
    {
        var leg = new Leg(Id, fromStationId, toStationId, sequenceOrder, estimatedCost);
        _legs.Add(leg);
        EstimatedDistance += estimatedCost;
        return leg;
    }

    public void AssignVehicle(Guid vehicleId, double estimatedDuration)
    {
        if (Status != JobStatus.Created)
            throw new InvalidOperationException("Only jobs in Created status can be assigned.");

        AssignedVehicleId = vehicleId;
        EstimatedDuration = estimatedDuration;
        Status = JobStatus.Assigned;

        AddDomainEvent(new JobAssignedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, vehicleId));
    }

    public void Commit()
    {
        if (Status != JobStatus.Assigned && Status != JobStatus.Created)
            throw new InvalidOperationException("Only Created or Assigned jobs can be committed.");

        Status = JobStatus.Committed;
        var legs = _legs
            .OrderBy(l => l.SequenceOrder)
            .Select(l => new CommittedLegSnapshot(l.FromStationId, l.ToStationId, l.SequenceOrder))
            .ToList();

        AddDomainEvent(new JobCommittedDomainEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            TenantId,
            Id,
            AssignedVehicleId,
            legs));
    }

    /// <summary>
    /// Replan: reset a committed job so it can be re-assigned to a different vehicle.
    /// </summary>
    public void Replan(string reason)
    {
        if (Status != JobStatus.Committed && Status != JobStatus.Assigned)
            throw new InvalidOperationException($"Cannot replan a job in {Status} status.");

        AssignedVehicleId = null;
        EstimatedDuration = 0;
        Status = JobStatus.Created;
    }
}
