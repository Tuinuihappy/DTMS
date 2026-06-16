using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;

// Phase OMS B4 — Body of POST /api/shipments/{shipmentId}/pod-completed.
// Fired after the POD scan completes (separate stage from /arrived,
// which only confirms physical arrival at the drop station).
// scannedIds = lot identifiers actually scanned at delivery; may differ
// from the originally-loaded set if the operator skipped any lots.
public sealed record OmsPodCompletedNotification(
    [property: JsonPropertyName("scannedIds")] IReadOnlyList<string> ScannedIds,
    [property: JsonPropertyName("scannedAt")] DateTime ScannedAt);
