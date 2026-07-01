using System.Security.Claims;
using System.Text.Json;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Repositories;

namespace DTMS.Api.Middlewares;

/// <summary>
/// Inbound authentication for federated source-system callers. Only
/// branched in for paths under <c>/api/v1/source/*</c> (registered via
/// <c>app.UseWhen</c>) so user traffic never pays the lookup cost. On
/// a successful match the middleware stashes a
/// <see cref="SystemPrincipal"/> into <c>HttpContext.Items["principal"]</c>
/// — the ActorContext resolver and permission claims transformer
/// then pick it up exactly like any other authenticated request.
/// </summary>
/// <remarks>
/// <para><b>Auth scheme.</b> Single scheme — <c>bearer-jwt</c> (OAuth 2.0
/// client_credentials grant per RFC 6749 §4.4). Partners exchange a
/// long-lived client_secret at <c>POST /oauth/token</c> for a short-lived
/// JWT (1 hour default), then present it as <c>Authorization: Bearer ...</c>
/// here. The legacy <c>api-key</c> scheme was removed at production launch
/// — no backward compat needed (no production partners existed yet).</para>
/// </remarks>
public sealed class SystemClientAuthMiddleware : IMiddleware
{
    public const string PrincipalItemKey = "principal";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly CachedCredentialReader _credentials;
    private readonly ISystemClientRepository _clients;
    private readonly ISystemJwtValidator _jwtValidator;
    private readonly ILogger<SystemClientAuthMiddleware> _log;

    public SystemClientAuthMiddleware(
        CachedCredentialReader credentials,
        ISystemClientRepository clients,
        ISystemJwtValidator jwtValidator,
        ILogger<SystemClientAuthMiddleware> log)
    {
        _credentials = credentials;
        _clients = clients;
        _jwtValidator = jwtValidator;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Route shape: /api/v1/source/{key}/...
        // Parse the {key} segment without re-running the full template
        // matcher — we own the route prefix at the UseWhen layer.
        if (!TryExtractSystemKey(context.Request.Path, out var systemKey))
        {
            await Reject(context, StatusCodes.Status404NotFound, "source key missing");
            return;
        }

        var client = await _clients.GetByKeyAsync(systemKey, context.RequestAborted);
        if (client is null || !client.IsActive)
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "unknown or inactive source system");
            return;
        }

        var credential = await _credentials.GetAsync(systemKey, context.RequestAborted);
        if (credential is null)
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "no credential configured");
            return;
        }

        // OAuth client_credentials grant only. Partner first POSTs to
        // /oauth/token to exchange client_id+secret for a short-lived JWT,
        // then presents it here as Authorization: Bearer ...
        // Any other AuthScheme value in the DB indicates a misconfiguration
        // (the admin API only ever writes "bearer-jwt") — reject 401 rather
        // than silently allow.
        bool authenticated = credential.AuthScheme switch
        {
            "bearer-jwt" => TryAuthenticateJwt(context, systemKey),
            _ => false,
        };

        if (!authenticated)
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "credential rejected");
            return;
        }

        var principal = new SystemPrincipal(client.Key, client.DisplayName);
        context.Items[PrincipalItemKey] = principal;

        // Phase S.2.3 — load permission codes and stamp them as claims
        // on a fresh ClaimsPrincipal so .RequirePermission(...) works
        // for system callers exactly like it does for user callers.
        // IClaimsTransformation runs before this middleware (it's keyed
        // to the JwtBearer scheme), so we transform directly here
        // instead of plumbing system principals through that pipeline.
        var permCodes = await _clients.GetPermissionCodesAsync(client.Key, context.RequestAborted);
        var identity = new ClaimsIdentity(
            authenticationType: "SystemBearerJwt",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, principal.PrincipalId));
        identity.AddClaim(new Claim(ClaimTypes.Name, principal.DisplayName));
        foreach (var code in permCodes)
            identity.AddClaim(new Claim(PermissionClaimsTransformer.PermissionClaimType, code));
        context.User = new ClaimsPrincipal(identity);

        await next(context);
    }

    private static bool TryExtractSystemKey(PathString path, out string key)
    {
        // PathString.Value is "/api/v1/source/oms/..." here.
        ReadOnlySpan<char> v = path.Value.AsSpan();
        const string prefix = "/api/v1/source/";
        if (!v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            key = string.Empty;
            return false;
        }
        var after = v[prefix.Length..];
        int slash = after.IndexOf('/');
        var segment = slash < 0 ? after : after[..slash];
        if (segment.IsEmpty)
        {
            key = string.Empty;
            return false;
        }
        key = segment.ToString();
        return true;
    }

    private bool TryAuthenticateJwt(HttpContext ctx, string urlSystemKey)
    {
        // Header convention: Authorization: Bearer <jwt>
        var header = ctx.Request.Headers.Authorization.ToString();
        const string scheme = "Bearer ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var token = header[scheme.Length..].Trim();
        if (token.Length == 0)
            return false;

        var result = _jwtValidator.Validate(token);
        if (!result.IsValid)
        {
            _log.LogWarning(
                "bearer-jwt rejected for url system '{Url}': {Reason}",
                urlSystemKey, result.FailureReason);
            return false;
        }

        // Token-substitution guard: a token minted for system "oms" must not
        // unlock /api/v1/source/sap/*. The validator extracted the sub claim;
        // we compare it to the URL segment the middleware already parsed.
        // Both come from authenticated channels (sub via RSA-signed JWT, url
        // via routing) so a mismatch is always an attack or a misconfigured
        // partner — never a benign edge case.
        if (!string.Equals(result.SystemKey, urlSystemKey, StringComparison.Ordinal))
        {
            _log.LogWarning(
                "bearer-jwt sub mismatch: token sub='{Sub}' but url='{Url}'.",
                result.SystemKey, urlSystemKey);
            return false;
        }

        return true;
    }

    private static async Task Reject(HttpContext ctx, int statusCode, string reason)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/problem+json";
        var body = JsonSerializer.Serialize(new { error = reason }, JsonOpts);
        await ctx.Response.WriteAsync(body);
    }

}
