using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;

// Phase OMS B4 — Body of POST /api/shipments/{shipmentId}/failed.
// shipmentId is in the URL path (same convention as /arrived); body
// carries the failure detail. failureCategory is the structured
// JobFailureCategory enum (b13) so the OMS receiver can route by class
// (vendor / robot / customer / system); failureReason is the free-text
// for ops review.
public sealed record OmsTripFailedNotification(
    [property: JsonPropertyName("failureReason")] string FailureReason,
    [property: JsonPropertyName("failureCategory")] string FailureCategory,
    [property: JsonPropertyName("occurredAt")] DateTime OccurredAt);
