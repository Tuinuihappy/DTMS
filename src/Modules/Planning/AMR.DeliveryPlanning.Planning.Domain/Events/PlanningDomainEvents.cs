using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Events;

public record JobCreatedDomainEvent(Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId) : IDomainEvent;
public record JobAssignedDomainEvent(Guid EventId, DateTime OccurredOn, Guid JobId, Guid VehicleId) : IDomainEvent;
public record JobCommittedDomainEvent(Guid EventId, DateTime OccurredOn, Guid JobId) : IDomainEvent;
