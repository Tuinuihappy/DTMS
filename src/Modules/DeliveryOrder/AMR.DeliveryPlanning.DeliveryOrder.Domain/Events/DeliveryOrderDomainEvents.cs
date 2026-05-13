using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;

public record ItemEventDto(
    string Sku,
    double WeightKg,
    Guid PickupStationId,
    Guid DropStationId);

public record DeliveryOrderDraftedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderSubmittedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderValidatedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderCancelledDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderReadyToPlanDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid OrderId,
    string Priority,
    DateTime? Deadline,
    IReadOnlyList<ItemEventDto> Items) : IDomainEvent;
public record DeliveryOrderPlanningStartedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderPlannedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderDispatchedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderInProgressDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderHeldDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderReleasedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderFailedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderAmendedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderCompletedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderDraftUpdatedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
