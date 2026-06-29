using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Repositories;

namespace DTMS.Api.Middlewares;

/// <summary>
/// Phase S.2 inbound authentication for federated source-system
/// callers. Only branched in for paths under <c>/api/v1/source/*</c>
/// (registered via <c>app.UseWhen</c>) so user traffic never pays
/// the lookup cost. On a successful match the middleware stashes a
/// <see cref="SystemPrincipal"/> into <c>HttpContext.Items["principal"]</c>
/// — the ActorContext resolver and permission claims transformer
/// then pick it up exactly like any other authenticated request.
/// </summary>
/// <remarks>
/// <para><b>Auth scheme support.</b> The plan lists three schemes
/// (api-key, bearer-jwt, hmac). Phase S.2 ships <c>api-key</c> first
/// because it's the smallest viable surface to validate the full
/// pipeline end-to-end; bearer-jwt + hmac slot in as additional
/// branches without changing the middleware shape.</para>
///
/// <para><b>Hashing.</b> The stored <c>AuthConfig</c> for api-key
/// holds <c>{"keyHash":"&lt;sha256-hex&gt;"}</c>; the wire-format
/// API key is hashed with SHA-256 and compared in constant time so
/// a timing oracle can't reveal partial matches.</para>
/// </remarks>
public sealed class SystemClientAuthMiddleware : IMiddleware
{
    public const string PrincipalItemKey = "principal";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly CachedCredentialReader _credentials;
    private readonly ISystemClientRepository _clients;
    private readonly ILogger<SystemClientAuthMiddleware> _log;

    public SystemClientAuthMiddleware(
        CachedCredentialReader credentials,
        ISystemClientRepository clients,
        ILogger<SystemClientAuthMiddleware> log)
    {
        _credentials = credentials;
        _clients = clients;
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

        bool authenticated = credential.AuthScheme switch
        {
            "api-key" => TryAuthenticateApiKey(context, credential),
            // Future schemes drop in here without touching the surrounding
            // shape. Plan v4 calls out bearer-jwt + hmac as deferred.
            _ => false,
        };

        if (!authenticated)
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "credential rejected");
            return;
        }

        context.Items[PrincipalItemKey] = new SystemPrincipal(client.Key, client.DisplayName);
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

    private bool TryAuthenticateApiKey(HttpContext ctx, CachedCredential credential)
    {
        // Header convention: Authorization: ApiKey <key>
        var header = ctx.Request.Headers.Authorization.ToString();
        const string scheme = "ApiKey ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var presented = header[scheme.Length..].Trim();
        if (presented.Length == 0)
            return false;

        ApiKeyConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<ApiKeyConfig>(credential.AuthConfig, JsonOpts);
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Malformed AuthConfig for api-key scheme on system {Key}", credential.SystemKey);
            return false;
        }
        if (cfg is null || string.IsNullOrEmpty(cfg.KeyHash))
            return false;

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(presented), hash);
        var presentedHex = Convert.ToHexString(hash);

        // Constant-time comparison — defeats timing oracles that would
        // otherwise leak the hash prefix one byte at a time.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(presentedHex),
            Encoding.ASCII.GetBytes(cfg.KeyHash.ToUpperInvariant()));
    }

    private static async Task Reject(HttpContext ctx, int statusCode, string reason)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/problem+json";
        var body = JsonSerializer.Serialize(new { error = reason }, JsonOpts);
        await ctx.Response.WriteAsync(body);
    }

    private sealed class ApiKeyConfig
    {
        public string KeyHash { get; set; } = string.Empty;
    }
}
