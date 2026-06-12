using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Events;

public record JobCreatedDomainEvent(Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId) : IDomainEvent;
public record JobAssignedDomainEvent(Guid EventId, DateTime OccurredOn, Guid JobId, Guid VehicleId) : IDomainEvent;
public record CommittedLegSnapshot(Guid FromStationId, Guid ToStationId, int SequenceOrder);
public record JobCommittedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid? VehicleId,
    IReadOnlyCollection<CommittedLegSnapshot> Legs) : IDomainEvent;

public record JobDispatchedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid TripId,
    string? VendorOrderKey,
    int AttemptNumber) : IDomainEvent;

public record JobFailedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    string Reason,
    int AttemptNumber) : IDomainEvent;

// Phase b9 — Trip lifecycle events that update Job status.
public record JobExecutingDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid TripId) : IDomainEvent;

public record JobCompletedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid TripId) : IDomainEvent;

public record JobCancelledDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid TripId,
    string Reason) : IDomainEvent;
