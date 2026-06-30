using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DTMS.Api.OpenApi;

/// <summary>
/// OpenAPI document transformer that registers a single Authorization header
/// security scheme so Scalar/Swagger UI renders an "Authorize" button.
/// Without this, calls made from the docs UI go out without an Authorization
/// header and every protected endpoint returns 401.
///
/// <para>Phase S.6 — DTMS has TWO auth schemes at runtime: Bearer (user JWT
/// from External Auth, per ADR-014) and ApiKey (system credential generated
/// + verified by us). They share the <c>Authorization</c> header but with
/// different scheme prefixes. To support both from one Swagger Authorize
/// field we declare the scheme as <see cref="SecuritySchemeType.ApiKey"/> in
/// the header — Swagger then sends the value VERBATIM with no auto-prefix.
/// Callers paste the FULL header value including the scheme word
/// (<c>Bearer ...</c> or <c>ApiKey ...</c>), and the matching backend
/// handler downstream picks up its own scheme prefix.</para>
///
/// <para>The original Http+Bearer scheme auto-prefixed the value with
/// <c>Bearer </c>, which made pasting a system <c>ApiKey ...</c> impossible
/// from the UI. The current shape lets one Authorize field cover both
/// principal types without conflating them at the backend.</para>
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
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "Authorization",
            Description = """
                Paste the FULL Authorization header value, INCLUDING the scheme prefix:

                - User (JWT from External Auth):   `Bearer <jwt>`
                - System (Phase S.2 / S.6 key):    `ApiKey dtms_<systemKey>_<plaintext>`

                Swagger sends the value verbatim — do not omit the scheme word.

                Switching identities (e.g. user → system, or rotating a system key):
                click `Logout` first to clear the stored value, then paste the new
                full header. Otherwise Swagger keeps sending the previous one and
                every request returns 401.
                """,
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
        });

        return Task.CompletedTask;
    }
}
