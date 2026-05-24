using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AMR.DeliveryPlanning.DeliveryOrder.Presentation.Idempotency;

public static class RequireIdempotencyKeyExtensions
{
    /// <summary>
    /// Attach <see cref="IdempotencyKeyFilter"/> to a mutation endpoint and
    /// document the required header on the OpenAPI operation. Apply via
    /// <c>group.MapPost(...).RequireIdempotencyKey()</c>.
    /// </summary>
    public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder)
    {
        // OpenAPI annotation for the required header lives in the Api project
        // (where Microsoft.AspNetCore.OpenApi is referenced). The filter itself
        // enforces the header at runtime regardless of swagger docs.
        return builder.AddEndpointFilter<IdempotencyKeyFilter>();
    }
}
