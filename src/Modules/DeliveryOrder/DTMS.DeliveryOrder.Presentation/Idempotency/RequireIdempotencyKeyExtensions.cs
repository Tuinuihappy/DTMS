using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DTMS.DeliveryOrder.Presentation.Idempotency;

public static class RequireIdempotencyKeyExtensions
{
    /// <summary>
    /// Attach <see cref="IdempotencyKeyFilter"/> to a mutation endpoint and
    /// tag it with <see cref="IdempotencyKeyRequiredMetadata"/> so the OpenAPI
    /// operation transformer in the Api project documents the header.
    /// Apply via <c>group.MapPost(...).RequireIdempotencyKey()</c>.
    /// </summary>
    /// <param name="enforced">
    /// When true the header is mandatory and a request without one is rejected
    /// with 400 before the handler runs. Use this on endpoints whose
    /// create-semantics depend on the caller being able to retry safely — the
    /// alternative is the handler inventing its own replay rule out of a
    /// business field, which is what the source-order path used to do.
    /// Defaults to false so existing callers keep the best-effort behaviour.
    /// </param>
    public static RouteHandlerBuilder RequireIdempotencyKey(
        this RouteHandlerBuilder builder, bool enforced = false)
    {
        return builder
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .WithMetadata(enforced
                ? IdempotencyKeyRequiredMetadata.Enforced
                : IdempotencyKeyRequiredMetadata.Optional);
    }
}
