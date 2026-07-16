namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// Phase S.5 — enriched inputs the shipment fan-out consumers hand to whichever
/// shipment formatter the subscriber's <c>PayloadFormatKey</c> names. Plain
/// strings only; the formatter owns the wire shape. The fan-out resolves the
/// pieces the raw integration event doesn't carry: <c>ShipmentId</c> = root trip
/// id (walking the retry chain), <c>DeliveryBy</c> = the vendor vehicle name,
/// and the lot list from the order's items bound to the trip.
///
/// <para>Phase 5 — deliberately NOT named Oms*: this is the system-neutral
/// contract between the generic fan-out and any subscriber's formatter. A
/// sap/erp formatter consumes the same record; only the formatter differs.</para>
/// </summary>
public sealed record ShipmentStartedContext(
    string ShipmentId,
    string? DeliveryBy,
    IReadOnlyList<string> LotNos);

public sealed record ShipmentArrivedContext(
    string ShipmentId,
    IReadOnlyList<string> LotNos);

/// <summary>
/// Fields mirror the OmsTripCancelledNotification that DTMS posted to
/// <c>/api/shipments/{id}/cancelled</c> until 0f123c2 removed the outbound chain
/// (OMS had dropped the endpoint). Kept identical so re-enabling is a switch on
/// OMS's side rather than a new contract to agree on.
///
/// <para>Carries no lot list, unlike its siblings — deliberately, and the old
/// notification didn't either. TripCancelledConsumer unbinds the order's items
/// from the trip while handling the very same TripCancelledIntegrationEvent on
/// its own queue, so a lot lookup here races it and comes back empty whenever
/// that consumer commits first. A cancel keyed on the shipment id alone has
/// nothing to race.</para>
/// </summary>
public sealed record ShipmentCancelledContext(
    string ShipmentId,
    string Reason,
    string? CancelledBy,
    DateTime OccurredAt);
