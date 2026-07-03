using DTMS.SharedKernel.Domain;

namespace DTMS.Dispatch.IntegrationEvents;

// Schema 1.1 (Phase P0, 2026-06-14) — adds TriggeredBy + CorrelationId to
// every Trip lifecycle event so projection-based audits can answer
// "who/what triggered this transition" (vendor-webhook, ops user, scheduled
// reconciliation). Populated by DispatchDomainEventMapper from the ambient
// ICurrentActorContext at outbox-emit time. Nullable for backward compat —
// existing consumers (Phase b9 Job mirror, OMS notify) ignore them.

// VendorVehicleKey is the upstream device identifier (deviceKey RIOT3
// echoes on TASK_PROCESSING). Added in V1.1 — nullable, backward-compat
// for consumers that ignore it. Powers the Vehicle performance report,
// where it's the grouping dimension.
//
// Items (V1.2, Phase P5.3) — denormalized snapshot of every Item bound
// to this Trip at start, plus each item's owning Order context. Consumed
// by TripItemsProjector to materialize dispatch.TripItems. Caller in the
// vendor adapter loads this via ITripItemSnapshotProvider before calling
// Trip.MarkVendorStarted. Null/empty is valid for legacy/test paths —
// TripItemsProjector records the empty binding and waits for a future
// enrichment event.
public record TripStartedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid VehicleId,
    Guid DeliveryOrderId,
    string? VendorVehicleKey = null,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    IReadOnlyList<TripItemSnapshot>? Items = null) : IIntegrationEvent;

// WMS PR-4b — Manual/Fleet pool dispatch. Fires when Trip enters the
// available-pool state (Status → Dispatched). Consumed by:
//   • TripStartedOmsNotifyConsumer — notifies OMS with DeliveryBy=null
//     (no vehicle/operator yet — Manual pool doesn't bind at dispatch).
//   • TripPoolBroadcaster (SignalR) — pushes to operator PWAs so they
//     can Acknowledge and start immediately without polling (PR-C).
public record TripDispatchedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    IReadOnlyList<TripItemSnapshot>? Items = null,
    string SchemaVersion = "1.0") : IIntegrationEvent;

// Wire shape for a single Item-on-Trip binding. ItemPk is the
// deliveryorder.Items.Id PK; LotNo is the operator-facing identifier
// (Items.ItemId). OrderRef/OrderStatus are snapshotted at trip-start
// and never refreshed (per P5.3 design — operator can re-fetch order
// state via OrderId if they need live status).
//
// Description / QuantityValue / QuantityUom (V1.3) — optional display
// enrichment so the trip-items table can render fulfilment context
// without a second hop to the DeliveryOrder side. Nullable for
// backward compatibility with pre-V1.3 emitters.
public sealed record TripItemSnapshot(
    Guid ItemPk,
    int ItemSeq,
    string LotNo,
    string ItemStatus,
    string PickupCode,
    string DropCode,
    double? WeightKg,
    Guid DeliveryOrderId,
    string OrderRef,
    string OrderStatus,
    string? Description = null,
    double? QuantityValue = null,
    string? QuantityUom = null,
    // Order-level routing mode (Amr/Manual/Fleet) captured at trip-start
    // so operators see the dispatched mode without a live join to the
    // DeliveryOrder side. Nullable because the source field is nullable
    // and pre-V1.4 snapshots carry NULL.
    string? OrderTransportMode = null);

public record TripPickupCompletedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null) : IIntegrationEvent;

// RequiresDropPod (V1.2) — order's POD policy at drop-completion time.
// False → DeliveryOrder lands items at Delivered immediately and the
// TripItems projector skips DroppedOff; true → items hold at DroppedOff
// pending operator /pod-scan. Nullable: pre-rollout in-flight events
// have null here, and consumers fall back to reading Order.RequiresDropPod.
public record TripDropCompletedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid DeliveryOrderId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    bool? RequiresDropPod = null) : IIntegrationEvent;

// VendorUpperKey is the composite envelope correlation key
// (see EnvelopeUpperKey) that RIOT3 echoes back on every webhook.
public record TripCompletedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid DeliveryOrderId,
    string VendorUpperKey,
    string? TriggeredBy = null,
    Guid? CorrelationId = null) : IIntegrationEvent;

public record TripFailedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid DeliveryOrderId,
    string Reason, string VendorUpperKey,
    string? TriggeredBy = null,
    Guid? CorrelationId = null) : IIntegrationEvent;

public record TripCancelledIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid DeliveryOrderId, string Reason, string? VendorUpperKey,
    string? TriggeredBy = null,
    Guid? CorrelationId = null) : IIntegrationEvent;

public record ExceptionRaisedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid ExceptionId,
    string Code, string Severity, string Detail) : IIntegrationEvent;

public record PodCapturedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid TripId,
    Guid StopId,
    IReadOnlyList<string> ScannedIds) : IIntegrationEvent;

// Phase P1 (b12) — pause/resume transitions surface to the projector so
// the Trip status timeline covers every state in the TripStatus enum
// (Created/InProgress/Paused/Completed/Failed/Cancelled). No existing
// consumer reacts to these — only TripStatusHistoryProjector does today.
public record TripPausedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid TripId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

public record TripResumedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid TripId,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;

// Operator acknowledged a robot waiting at a checkpoint (RIOT3 PASS).
// Trip.Status remains InProgress — TripStatusHistoryProjector still
// appends a row so the timeline shows the operator's intervention.
// VendorVehicleKey is the deviceKey that was passed (used for the
// projector's Reason text + audit).
public record TripRobotPassAcknowledgedIntegrationEventV1(
    Guid EventId, DateTime OccurredOn, Guid TripId, string VendorVehicleKey,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    string SchemaVersion = "1.1") : IIntegrationEvent;
