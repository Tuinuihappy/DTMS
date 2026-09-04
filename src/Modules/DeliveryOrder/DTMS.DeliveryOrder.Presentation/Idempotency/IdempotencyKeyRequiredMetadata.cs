namespace DTMS.DeliveryOrder.Presentation.Idempotency;

/// <summary>
/// Endpoint metadata marker attached by <see cref="RequireIdempotencyKeyExtensions.RequireIdempotencyKey"/>.
/// The OpenAPI operation transformer in the Api project looks for this marker to
/// document the <c>Idempotency-Key</c> header on the operation, and
/// <see cref="IdempotencyKeyFilter"/> reads <see cref="IsEnforced"/> to decide
/// whether a request may omit the header.
/// </summary>
public sealed class IdempotencyKeyRequiredMetadata
{
    /// <summary>Header is advertised and honoured, but a request may omit it.</summary>
    public static readonly IdempotencyKeyRequiredMetadata Optional = new(false);

    /// <summary>Header is mandatory — a request without one is rejected with 400.</summary>
    public static readonly IdempotencyKeyRequiredMetadata Enforced = new(true);

    /// <summary>
    /// Retained for callers written against the original single-instance shape;
    /// the historical behaviour was optional.
    /// </summary>
    public static readonly IdempotencyKeyRequiredMetadata Instance = Optional;

    public bool IsEnforced { get; }

    private IdempotencyKeyRequiredMetadata(bool isEnforced) => IsEnforced = isEnforced;
}
