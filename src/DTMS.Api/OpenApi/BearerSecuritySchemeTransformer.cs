using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DTMS.Api.OpenApi;

/// <summary>
/// OpenAPI document transformer that registers the Bearer JWT security scheme
/// and applies it as the default requirement so Scalar/Swagger UI renders an
/// "Authorize" button. Without this, calls made from the docs UI go out
/// without an Authorization header and every protected endpoint returns 401.
/// Per ADR-014, tokens are issued by External Auth; this transformer only
/// declares the wire format DTMS expects.
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
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the JWT issued by External Auth (no \"Bearer \" prefix).",
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
        });

        return Task.CompletedTask;
    }
}
