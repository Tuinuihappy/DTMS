namespace AMR.DeliveryPlanning.DeliveryOrder.Presentation.Idempotency;

/// <summary>
/// Snapshot of a previously-served mutation response, keyed by Idempotency-Key.
/// <see cref="RequestHash"/> is compared against the current request to detect
/// a client sending the same key with a different body (-> 422).
/// </summary>
public sealed record IdempotencyCacheEntry(
    string RequestHash,
    int StatusCode,
    string? ContentType,
    string BodyBase64);
