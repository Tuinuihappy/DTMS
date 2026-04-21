using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Events;

public record TripStartedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId, Guid VehicleId) : IDomainEvent;
public record TripCompletedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TripId) : IDomainEvent;
public record TaskDispatchedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TaskId, Guid VehicleId) : IDomainEvent;
public record TaskCompletedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TaskId) : IDomainEvent;
public record TaskFailedDomainEvent(Guid EventId, DateTime OccurredOn, Guid TaskId, string Reason) : IDomainEvent;
