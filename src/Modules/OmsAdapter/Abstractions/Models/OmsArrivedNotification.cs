using System.Text.Json.Serialization;

namespace DTMS.OmsAdapter.Abstractions.Models;

// Body of POST /api/shipments/{shipmentId}/arrived. The shipmentId is in
// the URL path — only the lots travel in the JSON body, unlike the
// /shipments (started) call which carries shipmentId + deliveryBy too.
public sealed record OmsArrivedNotification(
    [property: JsonPropertyName("lots")] IReadOnlyList<OmsLot> Lots);
