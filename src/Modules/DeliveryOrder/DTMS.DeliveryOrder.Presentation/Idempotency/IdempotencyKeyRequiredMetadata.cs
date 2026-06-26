namespace AMR.DeliveryPlanning.DeliveryOrder.Presentation.Idempotency;

/// <summary>
/// Endpoint metadata marker attached by <see cref="RequireIdempotencyKeyExtensions.RequireIdempotencyKey"/>.
/// The OpenAPI operation transformer in the Api project looks for this marker to
/// document the required <c>Idempotency-Key</c> header on the operation.
/// </summary>
public sealed class IdempotencyKeyRequiredMetadata
{
    public static readonly IdempotencyKeyRequiredMetadata Instance = new();
}
