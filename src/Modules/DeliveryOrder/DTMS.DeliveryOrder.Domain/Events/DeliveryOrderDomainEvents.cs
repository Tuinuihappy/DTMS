using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Domain;

namespace DTMS.DeliveryOrder.Domain.Events;

public record ItemHazmatDto(string ClassCode, string? PackingGroup);

public record ItemTemperatureDto(double? MinC, double? MaxC);

// Phase 3a (multi-mode): station Ids are nullable — Manual / Fleet
// orders resolve location codes to warehouse Ids instead. Both pairs
// are nullable so the Created event can still fire pre-validation
// (when neither resolution has run) and consumers can pick whichever
// pair matches the order's RequestedTransportMode.
public record ItemEventDto(
    string ItemId,
    double WeightKg,
    Guid? PickupStationId,
    Guid? DropStationId,
    ItemHazmatDto? Hazmat = null,
    ItemTemperatureDto? Temperature = null,
    IReadOnlyList<string>? HandlingInstructions = null,
    Guid? PickupWarehouseId = null,
    Guid? DropWarehouseId = null);

public record DeliveryOrderDraftedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderSubmittedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderValidatedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;

// Phase P4.5 — fired once after items are populated so the OrderListView
// projection has the full snapshot to materialize a row. Raised by
// `DeliveryOrder.RaiseCreatedEvent()` from the command handler, NOT from
// `Create()`/`CreateFromUpstream()` (those fire at constructor time when
// items haven't been added yet).
public record DeliveryOrderCreatedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid OrderId,
    string OrderRef,
    string SourceSystem,
    string Status,
    string Priority,
    string? RequestedTransportMode,
    string? RequestedBy,
    string? CreatedBy,
    string? Notes,
    DateTime? EarliestUtc,
    DateTime? LatestUtc,
    DateTime? SubmittedAt,
    bool? RequiresDropPod,
    bool? RequiresPickupPod,
    int TotalItems,
    double TotalQuantity,
    double TotalWeightKg,
    IReadOnlyList<ItemEventDto> Items) : IDomainEvent;
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
// Phase S.3.1b follow-up — SourceSystem propagated so the IAM fan-out
// consumer can route the callback back to the order's originating
// system without taking a cross-module dependency on the DeliveryOrder
// repository. Nullable so calls that don't yet pass it (and any
// already-queued events) keep working — null routes to "no callback"
// (the source-routed model treats user-created orders as having no
// external system to notify).
public record DeliveryOrderCancelledDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason, SourceSystem? SourceSystem = null) : IDomainEvent;
public record DeliveryOrderPlanningStartedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderPlannedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderDispatchedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderInProgressDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderHeldDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderReleasedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
public record DeliveryOrderFailedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderReopenedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderRedispatchedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record TripItemsAssignedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, Guid TripId, int AttemptNumber, int ItemCount) : IDomainEvent;
public record TripItemsDeliveredDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, Guid TripId, int DeliveredCount) : IDomainEvent;
public record TripItemsFailedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, Guid TripId, int FailedCount, string Reason) : IDomainEvent;
public record TripItemsPickedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, Guid TripId, int PickedCount) : IDomainEvent;
public record TripItemsDroppedOffDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, Guid TripId, int DroppedCount) : IDomainEvent;
public record ItemPodRecordedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, Guid ItemId, PodScanType ScanType, string ScannedBy, string Method) : IDomainEvent;
public record DeliveryOrderAmendedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, string Reason) : IDomainEvent;
public record DeliveryOrderCompletedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId, SourceSystem? SourceSystem = null) : IDomainEvent;
public record DeliveryOrderPartiallyCompletedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid OrderId,
    int DeliveredCount,
    int NotDeliveredCount,
    int TotalItems) : IDomainEvent;
public record DeliveryOrderDraftUpdatedDomainEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IDomainEvent;
