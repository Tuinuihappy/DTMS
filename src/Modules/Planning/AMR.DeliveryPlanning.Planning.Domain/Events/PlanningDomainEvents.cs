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

// Phase #1 — Job mirrors Trip pause/resume state. Reason carries the
// upstream context (e.g. "Mirrored from Trip pause webhook") so the
// status-history timeline reads cleanly.
public record JobPausedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid TripId) : IDomainEvent;

public record JobResumedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid TripId) : IDomainEvent;
