using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Entities;

public class Job : AggregateRoot<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public JobStatus Status { get; private set; }
    public Guid? AssignedVehicleId { get; private set; }
    public string Priority { get; private set; } = "Normal";
    public double EstimatedDuration { get; private set; }
    public double EstimatedDistance { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private readonly List<Leg> _legs = new();
    public IReadOnlyCollection<Leg> Legs => _legs.AsReadOnly();

    private Job() { } // EF Core

    public Job(Guid deliveryOrderId, string priority)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = deliveryOrderId;
        Priority = priority;
        Status = JobStatus.Created;
        CreatedAt = DateTime.UtcNow;

        AddDomainEvent(new JobCreatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, DeliveryOrderId));
    }

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
        if (Status != JobStatus.Assigned)
            throw new InvalidOperationException("Only assigned jobs can be committed.");

        Status = JobStatus.Committed;
        AddDomainEvent(new JobCommittedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }
}
