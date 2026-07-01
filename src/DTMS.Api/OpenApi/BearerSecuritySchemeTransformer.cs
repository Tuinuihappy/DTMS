using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DTMS.Api.OpenApi;

/// <summary>
/// OpenAPI document transformer that registers a single Authorization header
/// security scheme so Scalar/Swagger UI renders an "Authorize" button.
/// Without this, calls made from the docs UI go out without an Authorization
/// header and every protected endpoint returns 401.
///
/// <para>Uses <see cref="SecuritySchemeType.Http"/> + scheme <c>bearer</c>
/// so Swagger auto-prefixes the value with <c>Bearer </c> — operators paste
/// the raw JWT (no need to remember the scheme word). This was
/// previously <c>ApiKey</c>+verbatim to support the legacy api-key path
/// alongside Bearer; that path was removed at Phase S.8 production
/// launch, so verbatim is no longer needed and the foot-gun of forgetting
/// the `Bearer ` prefix is gone.</para>
/// </summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        // We KEEP the "Bearer" name on the scheme so existing operation-level
        // [Authorize] OpenAPI annotations + clients that reference the
        // scheme by id continue to compile. Only the Type + behavior change.
        // Scalar UI doesn't auto-generate a description from the type +
        // scheme + bearerFormat triple (Swagger UI does — different quirk),
        // so provide the canonical one-liner explicitly. Onboarding details
        // live in docs/system-onboarding.md where they can breathe.
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT Authorization header using Bearer scheme",
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
        });

        return Task.CompletedTask;
    }
}
