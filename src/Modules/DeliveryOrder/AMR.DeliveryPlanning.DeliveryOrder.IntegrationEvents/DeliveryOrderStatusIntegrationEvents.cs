using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;

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
