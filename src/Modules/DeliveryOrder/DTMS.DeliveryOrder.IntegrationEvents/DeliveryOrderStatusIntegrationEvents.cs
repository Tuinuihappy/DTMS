using DTMS.SharedKernel.Domain;

namespace DTMS.DeliveryOrder.IntegrationEvents;

// All DeliveryOrder integration events carry an explicit SchemaVersion field
// (semver string). The class name encodes the major version; the field encodes
// minor (additive) revisions. Convention:
//   - Add nullable field → bump minor (V1, "1.0" → "1.1"), consumer keeps working.
//   - Remove field / rename / change type → bump major (V1 → V2 class), with
//     V1 retained until all consumers migrate.
//
// Schema 1.1 (Phase P0, 2026-06-14) — adds TriggeredBy + CorrelationId so
// projection-based audits can answer "who changed status X" without joining
// other tables. Populated by DeliveryOrderDomainEventMapper from the ambient
// ICurrentActorContext at outbox-emit time. Nullable so projectors that
// don't care can ignore them.

public record DeliveryOrderCancelledIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId, string Reason,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record DeliveryOrderFailedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId, string Reason,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record DeliveryOrderCompletedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record DeliveryOrderPartiallyCompletedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId,
    int DeliveredCount, int NotDeliveredCount, int TotalItems,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record DeliveryOrderAmendedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId, string Reason,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record DeliveryOrderHeldIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId, string Reason,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record DeliveryOrderReleasedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record DeliveryOrderRejectedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId, string Reason,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

// Option A: 4-state envelope flow visibility. Planning + Planned are
// internal-only (sub-second transitions, no value to downstream).
// Dispatched + InProgress have meaningful durations and surface to
// frontend / dashboards.
public record DeliveryOrderDispatchedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record DeliveryOrderInProgressIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

// Phase P4.5 (2026-06-15) — early-lifecycle events so the OrderListView
// projection can materialize rows for Draft / Submitted / Validated orders
// (previously invisible because the projector only consumed Confirmed).
// Created carries the full row-creation payload (same items shape as
// Confirmed); Submitted + Validated are status-only updates.

public record DeliveryOrderCreatedIntegrationEventV1(
    Guid EventId,
    DateTime OccurredOn,
    Guid DeliveryOrderId,
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
    IReadOnlyList<ItemSummaryDto> Items,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.0") : IIntegrationEvent;

public record DeliveryOrderSubmittedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.0") : IIntegrationEvent;

public record DeliveryOrderValidatedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.0") : IIntegrationEvent;

// Phase P4.6 (2026-06-25) — Draft replace via PUT carries no status change,
// but mutates items + totals + service window + notes / transport mode.
// Surfaced so the OrderListView projection can refresh the row instead of
// drifting until the next status transition.
public record DeliveryOrderDraftUpdatedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.0") : IIntegrationEvent;
