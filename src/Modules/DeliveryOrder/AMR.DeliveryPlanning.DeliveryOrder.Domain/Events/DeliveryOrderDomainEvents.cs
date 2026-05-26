using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;

public record ItemHazmatDto(string ClassCode, string? PackingGroup);

public record ItemTemperatureDto(double? MinC, double? MaxC);

public record ItemEventDto(
    string Sku,
    double WeightKg,
    Guid PickupStationId,
    Guid DropStationId,
    ItemHazmatDto? Hazmat = null,
    ItemTemperatureDto? Temperature = null,
    IReadOnlyList<string>? HandlingInstructions = null);

public record DeliveryOrderDraftedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderSubmittedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderValidatedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderConfirmedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid OrderId,
    string Priority,
    string SlaTier,
    DateTime? Earliest,
    DateTime? Latest,
    // Deadline removed in P1-8: external integration event is now V1-versioned
    // and the un-versioned Deadline alias has been dropped. Consumers should
    // read Latest (the upper window bound) instead.
    DateTime? SubmittedAt,
    IReadOnlyList<ItemEventDto> Items) : IDomainEvent;
public record DeliveryOrderRejectedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderCancelledDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
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
