using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Repositories;

namespace DTMS.Api.Auth;

/// <summary>
/// Phase S.8 — OAuth 2.0 token endpoint (RFC 6749 §4.4 client_credentials grant).
///
/// <para>Federated source systems whose <c>SystemCredential.AuthScheme = "bearer-jwt"</c>
/// POST their <c>client_id</c> + <c>client_secret</c> here and get back a
/// short-lived JWT signed by <see cref="ISystemJwtIssuer"/>. They then
/// present that JWT as <c>Authorization: Bearer ...</c> when calling
/// <c>/api/v1/source/*</c> — <see cref="DTMS.Api.Middlewares.SystemClientAuthMiddleware"/>
/// resolves identity from the JWT sub claim.</para>
///
/// <para><b>Why an endpoint here</b> rather than on the IAM module's
/// SystemAdminEndpoints: this is a public, anonymous OAuth surface, not an
/// admin tool. It lives next to the JwtBearer + JwtSettings wiring it
/// depends on, keeping the cross-module dependency chain shallow.</para>
///
/// <para><b>Error shapes</b> follow RFC 6749 §5.2 verbatim — partners using
/// any OAuth client library will surface our messages without translation.</para>
/// </summary>
public static class OauthTokenEndpoint
{
    public const string Path = "/oauth/token";

    public static IEndpointRouteBuilder MapOauthTokenEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost(Path, HandleAsync)
           .AllowAnonymous()
           .WithTags("OAuth")
           .WithSummary("OAuth 2.0 token endpoint (client_credentials grant)")
           .WithDescription(
                "Exchange a SystemClient's client_id + client_secret for a short-lived JWT. " +
                "Only systems with AuthScheme=bearer-jwt may use this endpoint; api-key " +
                "systems authenticate per-request via the Authorization header instead. " +
                "Accepts application/x-www-form-urlencoded per RFC 6749 §4.4.")
           // OpenAPI metadata — Accepts/Produces drive Scalar/Swagger UI so
           // the form fields render as a table and the success/error schemas
           // show up in the response panel. Schema-only (no behavior change);
           // the actual handler reads Request.Form directly.
           .Accepts<TokenRequestForm>("application/x-www-form-urlencoded")
           .Produces<TokenResponse>(StatusCodes.Status200OK)
           .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
           .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary>
    /// Documentation-only DTO that tells OpenAPI what fields the form body
    /// carries. The handler reads <see cref="HttpContext.Request"/>.Form
    /// directly so the actual values don't go through model binding — this
    /// type never appears at runtime.
    /// </summary>
    public sealed class TokenRequestForm
    {
        /// <summary>Must be the literal string <c>client_credentials</c>.</summary>
        public string grant_type { get; set; } = "client_credentials";

        /// <summary>The SystemClient key, e.g. <c>oms</c>.</summary>
        public string client_id { get; set; } = string.Empty;

        /// <summary>The plaintext returned at create/rotate time
        /// (<c>dtms_cs_&lt;key&gt;_&lt;...&gt;</c>).</summary>
        public string client_secret { get; set; } = string.Empty;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext ctx,
        ISystemClientRepository systems,
        CachedCredentialReader credentials,
        ISystemJwtIssuer issuer,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var log = loggerFactory.CreateLogger("OauthTokenEndpoint");

        // Body MUST be form-encoded per RFC 6749 §3.2; refuse anything else so
        // a misconfigured partner gets a clear "wrong content type" rather
        // than a cryptic "invalid_request".
        if (!ctx.Request.HasFormContentType)
        {
            return TokenError(
                StatusCodes.Status400BadRequest,
                "invalid_request",
                "Content-Type must be application/x-www-form-urlencoded.");
        }

        var form = await ctx.Request.ReadFormAsync(ct);
        var grantType = form["grant_type"].ToString();
        var clientId = form["client_id"].ToString();
        var clientSecret = form["client_secret"].ToString();

        if (string.IsNullOrWhiteSpace(grantType))
            return TokenError(400, "invalid_request", "grant_type is required.");

        if (!string.Equals(grantType, "client_credentials", StringComparison.Ordinal))
            return TokenError(400, "unsupported_grant_type",
                $"Grant type '{grantType}' is not supported. Use 'client_credentials'.");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return TokenError(400, "invalid_request", "client_id and client_secret are required.");

        // ── Lookup + validate ────────────────────────────────────────────
        // SystemClient and SystemCredential are separate aggregates but share
        // the systemKey. We check both: client must exist + be active, and
        // credential must be configured for bearer-jwt. A system configured
        // for api-key cannot use this endpoint even if the caller knows the
        // hash — wrong tool for the job.

        var client = await systems.GetByKeyAsync(clientId, ct);
        if (client is null || !client.IsActive)
        {
            log.LogWarning("OAuth token denied: unknown or inactive client '{ClientId}'.", clientId);
            return TokenError(401, "invalid_client", "Unknown or inactive client.");
        }

        var credential = await credentials.GetAsync(clientId, ct);
        if (credential is null)
        {
            log.LogWarning("OAuth token denied: client '{ClientId}' has no credential row.", clientId);
            return TokenError(401, "invalid_client", "Unknown or inactive client.");
        }

        if (!string.Equals(credential.AuthScheme, "bearer-jwt", StringComparison.Ordinal))
        {
            log.LogWarning(
                "OAuth token denied: client '{ClientId}' is configured for scheme '{Scheme}', not bearer-jwt.",
                clientId, credential.AuthScheme);
            return TokenError(401, "invalid_client",
                "This client is not configured for the client_credentials grant.");
        }

        BearerJwtConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<BearerJwtConfig>(credential.AuthConfig, JsonOpts);
        }
        catch (JsonException ex)
        {
            log.LogError(ex, "Malformed bearer-jwt AuthConfig for client '{ClientId}'.", clientId);
            return TokenError(500, "server_error", "Credential is misconfigured. Contact admin.");
        }

        if (cfg is null || string.IsNullOrEmpty(cfg.ClientSecretHash))
        {
            log.LogError("bearer-jwt AuthConfig for client '{ClientId}' missing clientSecretHash.", clientId);
            return TokenError(500, "server_error", "Credential is misconfigured. Contact admin.");
        }

        // Constant-time compare on the hashes — defeats timing oracles that
        // could otherwise leak the hash prefix one byte at a time. We compare
        // upper-case hex strings so casing of the stored hash never causes
        // a silent mismatch.
        Span<byte> presentedHash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret), presentedHash);
        var presentedHex = Convert.ToHexString(presentedHash);

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(presentedHex),
            Encoding.ASCII.GetBytes(cfg.ClientSecretHash.ToUpperInvariant()));

        if (!matches)
        {
            log.LogWarning("OAuth token denied: secret mismatch for client '{ClientId}'.", clientId);
            return TokenError(401, "invalid_client", "Invalid client credentials.");
        }

        // ── Mint + return ───────────────────────────────────────────────
        var lifetimeOverride = cfg.TokenLifetimeSeconds > 0 ? cfg.TokenLifetimeSeconds : (int?)null;
        var token = issuer.Issue(clientId, lifetimeSecondsOverride: lifetimeOverride);

        log.LogInformation(
            "OAuth token issued: client={ClientId} expires_in={ExpiresIn}s",
            clientId, token.ExpiresInSeconds);

        return Results.Ok(new TokenResponse(
            AccessToken: token.AccessToken,
            TokenType: "Bearer",
            ExpiresIn: token.ExpiresInSeconds));
    }

    private static IResult TokenError(int status, string error, string description)
        => Results.Json(
            new ErrorResponse(error, description),
            statusCode: status,
            contentType: "application/json");

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed class BearerJwtConfig
    {
        public string ClientSecretHash { get; set; } = string.Empty;
        public int TokenLifetimeSeconds { get; set; }
    }

    // RFC 6749 §5.1 — successful response. snake_case field names are
    // mandated by the spec, so we override the project's default casing
    // explicitly rather than rely on global JsonOptions (which other
    // endpoints may flip without realising it affects /oauth/token).
    // Public so OpenAPI metadata (.Produces<...>) can reference it.
    public sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    // RFC 6749 §5.2 — error response. Same snake_case rule.
    public sealed record ErrorResponse(
        [property: JsonPropertyName("error")] string Error,
        [property: JsonPropertyName("error_description")] string ErrorDescription);
}
