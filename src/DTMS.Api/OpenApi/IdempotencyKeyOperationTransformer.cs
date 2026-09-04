using DTMS.DeliveryOrder.Presentation.Idempotency;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DTMS.Api.OpenApi;

/// <summary>
/// OpenAPI transformer that documents the required <c>Idempotency-Key</c> header
/// on every endpoint tagged with <see cref="IdempotencyKeyRequiredMetadata"/>.
/// Runtime enforcement happens in <c>IdempotencyKeyFilter</c>; this only adds
/// the header to the Swagger UI so callers can supply it without guessing.
/// </summary>
internal sealed class IdempotencyKeyOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var marker = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IdempotencyKeyRequiredMetadata>()
            .FirstOrDefault();

        if (marker is null) return Task.CompletedTask;

        operation.Parameters ??= new List<IOpenApiParameter>();

        if (operation.Parameters.Any(p =>
                p.In == ParameterLocation.Header &&
                string.Equals(p.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = marker.IsEnforced,
            Description = marker.IsEnforced
                ? "**Required.** Unique value (UUID recommended) per logical operation. "
                    + "Retries with the same key replay the original response; the same key "
                    + "with a different body returns 422. A request without this header is "
                    + "rejected with 400 — it is the only safe way to retry on this route, "
                    + "because reusing an existing orderRef returns 409 rather than replaying."
                : "Optional but **strongly recommended**. Unique value "
                    + "(UUID recommended) per logical operation. Retries with the same key "
                    + "replay the original response; the same key with a different body returns 422. "
                    + "If omitted, the request executes without replay protection — duplicates on "
                    + "network retries are the caller's risk.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid", MaxLength = 200 }
        });

        return Task.CompletedTask;
    }
}
