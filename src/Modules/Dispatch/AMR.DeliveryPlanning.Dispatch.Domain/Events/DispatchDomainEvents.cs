using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Events;

public record TripStartedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId, Guid? VehicleId) : IDomainEvent;
public record TripCompletedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid DeliveryOrderId) : IDomainEvent;
public record TripPausedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId) : IDomainEvent;
public record TripResumedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId) : IDomainEvent;
public record TripCancelledDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, string Reason) : IDomainEvent;
public record TripReassignedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId, Guid NewVehicleId) : IDomainEvent;
// Emitted the first time a vehicle is bound to a trip that was created
// without one (e.g. RIOT3 auto-selected the robot and reported it back
// via processingVehicle.key). Distinct from TripReassigned: no pending
// tasks are reset because the vendor is already executing them.
public record TripVehicleAssignedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId, Guid VehicleId) : IDomainEvent;
public record TaskDispatchedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TaskId, Guid? VehicleId) : IDomainEvent;
public record TaskCompletedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TaskId) : IDomainEvent;
public record TaskFailedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TaskId, string Reason) : IDomainEvent;
public record ExceptionRaisedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid TripId,
    Guid JobId,
    Guid ExceptionId,
    string Code,
    string Severity,
    string Detail) : IDomainEvent;
public record ExceptionResolvedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId, Guid ExceptionId, string Resolution) : IDomainEvent;
public record PodCapturedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid TripId,
    Guid StopId,
    IReadOnlyList<string> ScannedIds) : IDomainEvent;
