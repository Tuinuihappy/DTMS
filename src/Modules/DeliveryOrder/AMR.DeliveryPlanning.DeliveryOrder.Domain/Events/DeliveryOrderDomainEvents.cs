using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;

public record ItemHazmatDto(string ClassCode, string? PackingGroup);

public record ItemTemperatureDto(double? MinC, double? MaxC);

public record ItemEventDto(
    string ItemId,
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
    DateTime? EarliestUtc,
    DateTime? LatestUtc,
    DateTime? SubmittedAt,
    IReadOnlyList<ItemEventDto> Items,
    string? RequestedTransportMode = null) : IDomainEvent;
public record DeliveryOrderRejectedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderCancelledDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderPlanningStartedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderPlannedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderDispatchedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderInProgressDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderHeldDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderReleasedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderFailedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderReopenedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderAmendedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderCompletedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderPartiallyCompletedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid OrderId,
    int DeliveredCount,
    int NotDeliveredCount,
    int TotalItems) : IDomainEvent;
public record DeliveryOrderDraftUpdatedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
