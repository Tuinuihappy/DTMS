using System.Text.Json.Serialization;

namespace DTMS.OmsAdapter.Abstractions.Models;

// Wire-shape for upstream OMS POST /api/shipments. JsonPropertyName is
// explicit so the contract survives serializer-option drift across the
// repo (some consumers configure camelCase globally, others don't).
// WMS PR-4b — DeliveryBy is nullable so Manual/Fleet pool dispatch can
// notify OMS immediately at dispatch time (before any operator has
// claimed the trip → no vehicle to name). AMR trips continue to
// populate DeliveryBy with VendorVehicleName.
public sealed record OmsShipmentNotification(
    [property: JsonPropertyName("shipmentId")] string ShipmentId,
    [property: JsonPropertyName("deliveryBy")] string? DeliveryBy,
    [property: JsonPropertyName("lots")] IReadOnlyList<OmsLot> Lots);
