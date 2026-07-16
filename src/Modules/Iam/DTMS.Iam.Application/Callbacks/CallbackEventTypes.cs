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
    /// <summary>Order has finished delivery successfully — terminal happy path.</summary>
    public const string OrderDeliveredV1 = "order.delivered.v1";

    /// <summary>Order was cancelled before completion.</summary>
    public const string OrderCancelledV1 = "order.cancelled.v1";

    /// <summary>Shipment started — trip Created → InProgress (Phase S.5, was the
    /// legacy OMS <c>POST /api/shipments</c>).</summary>
    public const string ShipmentStartedV1 = "shipment.started.v1";

    /// <summary>Shipment arrived at the drop station (Phase S.5, was the legacy
    /// OMS <c>POST /api/shipments/{id}/arrived</c>).</summary>
    public const string ShipmentArrivedV1 = "shipment.arrived.v1";

    /// <summary>A started shipment's trip was cancelled. Trip-scoped like its
    /// started/arrived siblings, so the shipmentId is the same root trip id
    /// they carry — the order-scoped <see cref="OrderCancelledV1"/> cannot
    /// address an OMS shipment (an order spans N root trips).
    ///
    /// <para>NOT terminal: a retry reuses the root trip id, so a subscriber can
    /// legitimately see started(X) → cancelled(X) → started(X). Cancellation is
    /// operator-driven and a retry may follow minutes later, so "no retry will
    /// follow" is unknowable at send time.</para></summary>
    public const string ShipmentCancelledV1 = "shipment.cancelled.v1";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        OrderDeliveredV1,
        OrderCancelledV1,
        ShipmentStartedV1,
        ShipmentArrivedV1,
        ShipmentCancelledV1,
    };

    public static bool IsKnown(string eventType) => All.Contains(eventType);
}
