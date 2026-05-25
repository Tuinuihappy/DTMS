using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AMR.DeliveryPlanning.DeliveryOrder.Presentation.Idempotency;

public static class RequireIdempotencyKeyExtensions
{
    /// <summary>
    /// Attach <see cref="IdempotencyKeyFilter"/> to a mutation endpoint and
    /// tag it with <see cref="IdempotencyKeyRequiredMetadata"/> so the OpenAPI
    /// operation transformer in the Api project documents the required header.
    /// Apply via <c>group.MapPost(...).RequireIdempotencyKey()</c>.
    /// </summary>
    public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder)
    {
        return builder
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .WithMetadata(IdempotencyKeyRequiredMetadata.Instance);
    }
}
