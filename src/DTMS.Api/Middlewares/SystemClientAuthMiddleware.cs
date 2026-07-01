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
///
/// <para><b>Phase S.8e (P3) — JWT-only identity.</b> Order of operations:
/// <list type="number">
///   <item>Extract Bearer token; missing/blank → 401.</item>
///   <item>Validate JWT signature + exp; failure → 401 (no lookups yet).</item>
///   <item>Take <c>SystemKey</c> from the validated <c>sub</c> claim — the
///     sole source of identity.</item>
///   <item>Load SystemClient row via that key; missing/inactive → 401.</item>
///   <item>Load SystemCredential row (checks the client actually has an
///     auth config registered) — belt-and-suspenders against an admin
///     inserting a bare SystemClient row without minting a secret. The
///     <c>AuthScheme</c> column stays <c>bearer-jwt</c> for the same
///     reason a checkstamp does; if it's ever anything else that's an
///     admin misconfig and we reject rather than fall through.</item>
///   <item>Stamp <see cref="SystemPrincipal"/> + permission claims.</item>
/// </list>
/// The old URL-vs-<c>sub</c> comparison from Phases S.2 – S.8d
/// is gone. JWT sig verification already makes <c>sub</c>
/// authoritative — echoing it in the URL added wire redundancy without
/// changing what an attacker can do. The <c>/api/v1/source/{key}/*</c>
/// route shape is retired in favour of a single canonical URL per
/// operation.</para>
///
/// <para><b>Attack surface note.</b> A stream of forged Bearer tokens
/// aimed at <c>/api/v1/source/*</c> gets rejected before any Redis or
/// Postgres lookup, so an attacker can't use the endpoint to burn our
/// cache/DB budget the way the pre-P1 order allowed.</para>
/// </remarks>
public sealed class SystemClientAuthMiddleware : IMiddleware
{
    public const string PrincipalItemKey = "principal";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly CachedCredentialReader _credentials;
    private readonly CachedSystemClientReader _cachedClients;
    private readonly ISystemClientRepository _clients;
    private readonly ISystemJwtValidator _jwtValidator;
    private readonly ILogger<SystemClientAuthMiddleware> _log;

    public SystemClientAuthMiddleware(
        CachedCredentialReader credentials,
        CachedSystemClientReader cachedClients,
        ISystemClientRepository clients,
        ISystemJwtValidator jwtValidator,
        ILogger<SystemClientAuthMiddleware> log)
    {
        _credentials = credentials;
        _cachedClients = cachedClients;
        _clients = clients;
        _jwtValidator = jwtValidator;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Step 1 — extract Bearer. Pure header parse, no I/O, so a
        // header-less request never touches the cache.
        if (!TryExtractBearer(context, out var token))
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "missing bearer");
            return;
        }

        // Step 2 — validate signature + expiry via the SystemJwt public
        // key loaded at startup (no per-request I/O). Forged/expired
        // tokens die here so a flooder can't cost us a Redis hit.
        var validated = _jwtValidator.Validate(token);
        if (!validated.IsValid)
        {
            _log.LogWarning("bearer-jwt rejected: {Reason}", validated.FailureReason);
            await Reject(context, StatusCodes.Status401Unauthorized, "jwt invalid");
            return;
        }

        // Step 3 — the JWT sub claim is the identity. Anything downstream
        // (endpoint handlers, permission handler, request log, actor
        // context) reads off the SystemPrincipal we stamp below; the URL
        // path is not consulted for identity anywhere in the pipeline.
        var systemKey = validated.SystemKey;
        if (string.IsNullOrEmpty(systemKey))
        {
            _log.LogWarning("bearer-jwt valid but sub claim missing/empty");
            await Reject(context, StatusCodes.Status401Unauthorized, "jwt missing sub");
            return;
        }

        // Step 4 — verify the client row still exists and is active. The
        // admin API invalidates the L1/L2 cache on deactivate, so this
        // catches a just-revoked partner within one L1 TTL (60s) even if
        // their JWT hasn't expired yet.
        var client = await _cachedClients.GetAsync(systemKey, context.RequestAborted);
        if (client is null || !client.IsActive)
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "unknown or inactive source system");
            return;
        }

        // Step 5 — the credential row must exist and be bearer-jwt.
        // A missing credential means an out-of-band DB edit created a
        // client without a secret — treat as misconfig, not "valid
        // JWT means anything goes". A scheme other than bearer-jwt
        // means the same thing at a different layer (admin API only
        // writes bearer-jwt); either way, refuse rather than pretend.
        var credential = await _credentials.GetAsync(systemKey, context.RequestAborted);
        if (credential is null)
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "no credential configured");
            return;
        }
        if (!string.Equals(credential.AuthScheme, "bearer-jwt", StringComparison.Ordinal))
        {
            _log.LogWarning(
                "credential for '{Key}' has non-bearer-jwt scheme '{Scheme}' — refusing",
                systemKey, credential.AuthScheme);
            await Reject(context, StatusCodes.Status401Unauthorized, "credential scheme mismatch");
            return;
        }

        // Step 6 — stamp SystemPrincipal + permission claims. Downstream
        // reads from this principal; identity flows from JWT → principal
        // → whatever needs it, with no URL-parsing anywhere.
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

    private static bool TryExtractBearer(HttpContext ctx, out string token)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        const string scheme = "Bearer ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            token = string.Empty;
            return false;
        }
        token = header[scheme.Length..].Trim();
        return token.Length > 0;
    }

    private static async Task Reject(HttpContext ctx, int statusCode, string reason)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/problem+json";
        var body = JsonSerializer.Serialize(new { error = reason }, JsonOpts);
        await ctx.Response.WriteAsync(body);
    }
}
