using DTMS.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.IntegrationEvents;

public record JobAssignedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid VehicleId,
    Guid PickupStationId,
    Guid DropStationId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null) : IIntegrationEvent;

public record PlannedLegDto(
    Guid FromStationId,
    Guid ToStationId,
    int SequenceOrder);

public record PlanCommittedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid JobId,
    Guid DeliveryOrderId,
    Guid? VehicleId,
    List<PlannedLegDto> Legs,
    string? TriggeredBy = null,
    Guid? CorrelationId = null) : IIntegrationEvent;

// ── Phase P1 (b12) — Job status lifecycle integration events ───────────
// These bridge the existing JobXxxDomainEvent stream to anything that
// wants to react to a Job state change without coupling to the Planning
// domain assembly. The projector under Phase P1 is the first consumer.
// SchemaVersion follows the same convention as DeliveryOrder events:
// additive fields bump minor; breaking changes bump the class name.
//
// Schema 1.1 (Phase P0, 2026-06-14) — adds TriggeredBy + CorrelationId
// populated from ambient ICurrentActorContext at outbox-emit time.

public record JobCreatedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record JobDispatchedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    Guid TripId, string? VendorOrderKey, int AttemptNumber,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record JobExecutingIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    Guid TripId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record JobCompletedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    Guid TripId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

// FailureCategory added in V1.1 (Phase #9 — surfaces b13's structured
// classification on the BI side). String, not enum, to keep cross-module
// consumers free of a reference to Planning.Domain. Nullable so pre-b13
// events still parse cleanly.
public record JobFailedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    string Reason, int AttemptNumber,
    string? FailureCategory = null,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record JobCancelledIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId,
    Guid TripId, string Reason,
    // Today MarkCancelled always classifies as OperatorCancelled; the field
    // is here so JobFactsProjector can populate the column without a special
    // case in the projector.
    string? FailureCategory = null,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

// Phase #1 — Job mirrors Trip pause/resume state. Status-history
// projector subscribes to write Paused / Executing transitions to the
// timeline.
public record JobPausedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId, Guid TripId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record JobResumedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid JobId, Guid DeliveryOrderId, Guid TripId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

// T2 Phase 2 step 1 — fired by DeliveryOrderValidatedConsumer immediately
// after MarkOrderPlanning succeeds. Marks the "planning has been requested
// for this order" boundary for the saga state machine, which transitions
// AwaitingPlan → Planning on receipt. The legacy procedural consumer
// remains authoritative; this event is the saga's read-side signal during
// dual-run shadow mode (see crash-recovery plan section 3.3).
public record OrderPlanRequestedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.0") : IIntegrationEvent;
