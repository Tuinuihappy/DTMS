namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// Phase S.5 — enriched inputs the shipment fan-out consumers hand to the OMS
/// shipment formatters. Plain strings only (no OmsAdapter dependency) — the
/// formatter owns the wire shape. The fan-out resolves the pieces the raw
/// integration event doesn't carry: <c>ShipmentId</c> = root trip id (walking
/// the retry chain), <c>DeliveryBy</c> = the vendor vehicle name, and the lot
/// list from the order's items bound to the trip. Mirrors exactly what the
/// legacy TripStarted/TripDropCompleted OMS-notify consumers assembled.
/// </summary>
public sealed record OmsShipmentStartedContext(
    string ShipmentId,
    string? DeliveryBy,
    IReadOnlyList<string> LotNos);

public sealed record OmsShipmentArrivedContext(
    string ShipmentId,
    IReadOnlyList<string> LotNos);
