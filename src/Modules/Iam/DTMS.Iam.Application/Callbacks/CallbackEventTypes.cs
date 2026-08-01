namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// Phase S.3.1b — closed registry of integration-event identities that
/// the outbound callback pipeline can fan out. Stored verbatim in
/// <c>iam.SystemEventSubscriptions.EventType</c>, in the outbox row's
/// <c>Type</c> column, and in the <c>X-DTMS-Event-Type</c> header of
/// the outbound HTTP POST — one constant per identity keeps all three
/// in lockstep.
///
/// <para>Versioning convention: <c>name.v{n}</c>. Additive payload
/// fields keep the version (subscribers stay forward-compatible);
/// removed / renamed / re-typed fields bump the version and the old
/// name stays in <see cref="All"/> until every subscriber has migrated
/// off it.</para>
///
/// <para>Adding a new event = (a) add const here, (b) add it to
/// <see cref="All"/>, (c) write a fan-out consumer in
/// <c>DTMS.Api.Infrastructure.Callbacks</c> that subscribes to the
/// matching <c>IIntegrationEvent</c> (the MassTransit assembly scan
/// picks it up — no registration edit), (d) implement a formatter (or
/// reuse one) for each subscriber that wants the event, (e) seed the
/// subscription row in a migration.</para>
/// </summary>
public static class CallbackEventTypes
{
    // order.delivered.v1 / order.cancelled.v1 were removed 2026-07-17: the
    // order-scoped pair never had a subscriber, its fan-out consumers never
    // stamped RelatedOrderId (so outcomes could not be audited), and every
    // live integration is trip-scoped via the shipment.* family below. If an
    // order-scoped callback is ever really needed, reintroduce it with the
    // full chain: fan-out consumer + RelatedOrderId + outcome audit labels.

    /// <summary>Shipment started — trip Created → InProgress (Phase S.5; since
    /// 2026-08 OMS receives it at <c>POST /integrations/tms/shipments/started</c>).</summary>
    public const string ShipmentStartedV1 = "shipment.started.v1";

    /// <summary>Shipment picked up — vendor/operator reached the pickup point
    /// (2026-08; OMS receives it at
    /// <c>POST /integrations/tms/shipments/{id}/pickup-arrived</c>). Semantics
    /// per transport mode: AMR = robot arrived at the pickup dock (the only
    /// pre-completion signal RIOT3 emits), Manual = operator confirmed loading
    /// (geofence-verified). Self-managed orders never emit it — their pickup is
    /// auto-fired at dispatch and carries no physical meaning.</summary>
    public const string ShipmentPickedUpV1 = "shipment.pickedup.v1";

    /// <summary>Shipment dropped off at the drop station (2026-08; OMS receives
    /// it at <c>POST /integrations/tms/shipments/{id}/dropoff-arrived</c>).
    /// Renamed from <see cref="ShipmentArrivedV1"/> when OMS moved the route.
    /// Semantics per transport mode mirror pickedup: AMR = robot reached the
    /// drop dock, Manual = operator confirmed drop (geofence-verified);
    /// self-managed orders never emit it — their drop is reported INTO DTMS by
    /// the source system, so sending it back would only echo.</summary>
    public const string ShipmentDroppedOffV1 = "shipment.droppedoff.v1";

    /// <summary>TRANSITIONAL — retired 2026-08, renamed to
    /// <see cref="ShipmentDroppedOffV1"/>. Stays registered so outbox rows
    /// enqueued under the old name keep auditing and remain supersedable until
    /// the backlog drains; remove in the post-cutover cleanup commit.</summary>
    public const string ShipmentArrivedV1 = "shipment.arrived.v1";

    /// <summary>A started shipment's trip was cancelled. Trip-scoped like its
    /// started/arrived siblings, so the shipmentId is the same root trip id
    /// they carry — an order-scoped cancel event could not
    /// address an OMS shipment (an order spans N root trips).
    ///
    /// <para>NOT terminal: a retry reuses the root trip id, so a subscriber can
    /// legitimately see started(X) → cancelled(X) → started(X). Cancellation is
    /// operator-driven and a retry may follow minutes later, so "no retry will
    /// follow" is unknowable at send time.</para></summary>
    public const string ShipmentCancelledV1 = "shipment.cancelled.v1";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ShipmentStartedV1,
        ShipmentPickedUpV1,
        ShipmentDroppedOffV1,
        ShipmentArrivedV1,   // transitional — see the const's doc
        ShipmentCancelledV1,
    };

    public static bool IsKnown(string eventType) => All.Contains(eventType);
}
