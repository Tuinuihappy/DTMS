using AMR.DeliveryPlanning.Planning.Domain.Enums;
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

// Category added in Phase #9 — Job.MarkFailed already classifies the
// failure (see b13). Domain event carries the enum directly; the
// integration-event mapper converts to string at the module boundary.
public record JobFailedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    string Reason,
    int AttemptNumber,
    JobFailureCategory Category) : IDomainEvent;

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
    string Reason,
    JobFailureCategory Category) : IDomainEvent;
