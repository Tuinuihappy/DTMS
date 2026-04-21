using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;

public record DeliveryOrderSubmittedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string OrderKey) : IDomainEvent;
public record DeliveryOrderValidatedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderCancelledDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
