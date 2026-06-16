using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;

// Phase OMS B4 — Body of POST /api/shipments/{shipmentId}/cancelled.
// Distinct from /failed: cancellation is operator-driven (intentional),
// whereas failure is system-driven (incident). Receiver typically
// treats cancellation as final-no-retry, failure as retryable.
public sealed record OmsTripCancelledNotification(
    [property: JsonPropertyName("cancelReason")] string CancelReason,
    [property: JsonPropertyName("cancelledBy")] string? CancelledBy,
    [property: JsonPropertyName("occurredAt")] DateTime OccurredAt);
