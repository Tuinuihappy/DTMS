using System.Security.Cryptography;
using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Phase S.8 — RS256 implementation of <see cref="ISystemJwtValidator"/>.
/// Singleton: RSA + TokenValidationParameters are built once at
/// construction so per-request validation is a hot-path check.
///
/// <para><b>Validation surface</b>:
/// <list type="bullet">
///   <item>Signature against the configured public key</item>
///   <item><c>iss</c> matches options issuer</item>
///   <item><c>aud</c> matches options audience</item>
///   <item><c>exp</c> not in the past (30s clock skew, same as user-JWT
///   pipeline elsewhere in Program.cs)</item>
///   <item><c>sub</c> starts with the literal <c>system:</c> prefix —
///   guards against a user-JWT being replayed at /api/v1/source/* should
///   key material ever overlap</item>
/// </list>
/// Per-URL system-key matching (preventing token-substitution between
/// systems) happens in the middleware, not here, because only the
/// middleware sees the request path.</para>
/// </summary>
public sealed class SystemJwtValidator : ISystemJwtValidator, IDisposable
{
    private const string SubjectPrefix = "system:";

    private readonly RSA _rsa;
    private readonly TokenValidationParameters _tvp;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly ISystemJwtRevocationList _revocationList;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SystemJwtValidator> _log;

    public SystemJwtValidator(
        IOptions<SystemJwtIssuerOptions> opts,
        ISystemJwtRevocationList revocationList,
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<SystemJwtValidator> log)
    {
        _revocationList = revocationList;
        _scopeFactory = scopeFactory;
        _cache = cache;
        _log = log;
        var o = opts.Value;
        if (string.IsNullOrWhiteSpace(o.PublicKeyPem))
            throw new InvalidOperationException(
                "SystemJwtIssuerOptions.PublicKeyPem is empty. " +
                "Set Jwt__SystemSigningPublicKey env var with a PEM-encoded RSA public key.");

        _rsa = RSA.Create();
        try
        {
            _rsa.ImportFromPem(o.PublicKeyPem);
        }
        catch (Exception ex)
        {
            _rsa.Dispose();
            throw new InvalidOperationException(
                "Failed to parse SystemJwtIssuerOptions.PublicKeyPem as a PEM-encoded RSA key. " +
                "Expected SubjectPublicKeyInfo ('BEGIN PUBLIC KEY') or PKCS#1 ('BEGIN RSA PUBLIC KEY').", ex);
        }

        // Use IssuerSigningKeyResolver instead of the single
        // IssuerSigningKey field. JsonWebTokenHandler matches the JWT's
        // `kid` header against IssuerSigningKey.KeyId by string equality —
        // and through a quirk of RsaSecurityKey initializer-syntax
        // assignment, the KeyId we set here doesn't propagate to the
        // internal key collection the handler iterates (you get
        // "IDX10503: ... kid 'X' did not match any keys" even when
        // KeyId == kid). Resolver bypasses kid lookup and hands the key
        // back unconditionally — signature validation still happens
        // against the returned key, so a JWT signed by a different key
        // still fails with a different IDX error.
        //
        // Why we keep KeyId on the returned key at all: the validator
        // log line includes KeyId, so a key-mismatch trace says
        // "dtms-system-prod-v2" rather than an opaque internal hash —
        // useful when rotating keypairs.
        var signingKey = new RsaSecurityKey(_rsa) { KeyId = o.KeyId };
        _tvp = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = o.Issuer,
            ValidateAudience = true,
            ValidAudience = o.Audience,
            ValidateLifetime = true,
            // Phase S.8d — perpetual admin tokens carry no exp claim. Keep
            // ValidateLifetime on (so any token that DOES have exp still
            // expires) but stop *requiring* exp, else a no-exp token is
            // rejected with SecurityTokenNoExpirationException. Perpetual
            // tokens are additionally gated by the DB allowlist below, so
            // relaxing this here does not weaken revocability.
            RequireExpirationTime = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeyResolver = (_, _, _, _) => new[] { signingKey },
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    public SystemJwtValidationResult Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new SystemJwtValidationResult(false, null, "empty token");

        // JsonWebTokenHandler.ValidateTokenAsync is async on the surface but
        // for symmetric/RSA keys with no async resolver it runs synchronously
        // and the resulting Task is already completed — .GetAwaiter().GetResult()
        // is safe here and avoids forcing every middleware caller to async/await
        // a path that never actually awaits anything.
        var result = _handler.ValidateTokenAsync(token, _tvp).GetAwaiter().GetResult();
        if (!result.IsValid)
        {
            var msg = result.Exception?.GetType().Name ?? "unknown";
            _log.LogWarning("System JWT rejected: {Reason}", msg);
            return new SystemJwtValidationResult(false, null, msg);
        }

        if (!result.Claims.TryGetValue("sub", out var subObj) || subObj is not string sub)
            return new SystemJwtValidationResult(false, null, "missing sub");

        if (!sub.StartsWith(SubjectPrefix, StringComparison.Ordinal))
            return new SystemJwtValidationResult(false, null, "sub not a system principal");

        var systemKey = sub[SubjectPrefix.Length..];
        if (string.IsNullOrWhiteSpace(systemKey))
            return new SystemJwtValidationResult(false, null, "sub has empty system key");

        // Phase S.8d — a token with no exp claim is a perpetual admin token.
        // Its ONLY kill switch is revocation, so it MUST carry a jti we can
        // key on; refuse one that doesn't (should never happen — the issuer
        // always stamps jti — but a missing jti here would be an
        // unrevokable forever-token, the worst possible failure mode).
        var isPerpetual = !result.Claims.ContainsKey("exp");
        result.Claims.TryGetValue("jti", out var jtiObj);
        var jti = jtiObj as string;

        if (isPerpetual && string.IsNullOrEmpty(jti))
            return new SystemJwtValidationResult(false, null, "perpetual token missing jti");

        // Phase S.8c — revocation list check (fail-close on Redis outage).
        // Any exception from Redis is treated as "reject the request" —
        // the alternative (fail-open) would let a revoked token slip
        // through during a Redis blip, defeating the point of revocation.
        // GetAwaiter().GetResult() is used because the caller is a sync
        // middleware branch; Redis KeyExistsAsync is a single round-trip
        // (~1-2ms typical) so blocking is acceptable.
        if (!string.IsNullOrEmpty(jti))
        {
            try
            {
                if (_revocationList.IsRevokedAsync(jti).GetAwaiter().GetResult())
                {
                    _log.LogWarning(
                        "System JWT rejected: jti={Jti} on revocation list.",
                        jti);
                    return new SystemJwtValidationResult(false, null, "token revoked");
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "System JWT revocation check failed (Redis outage?) — failing closed for jti={Jti}.",
                    jti);
                return new SystemJwtValidationResult(false, null, "revocation check unavailable");
            }

            // Phase S.8d — perpetual tokens get a second, DURABLE gate: the
            // iam.SystemIssuedTokens row must still exist and be Active. Redis
            // above gives instant revoke, but a Redis flush/rebuild would
            // resurrect a revoked token — the DB is the source of truth that
            // survives that. Expiring tokens skip this (Redis + natural exp
            // already bound their blast radius) so the common OAuth path pays
            // no DB cost.
            if (isPerpetual)
            {
                bool active;
                try
                {
                    active = IsPerpetualTokenActive(jti);
                }
                catch (Exception ex)
                {
                    // Symmetric with the Redis path above — a DB outage on the
                    // durable gate fails CLOSED (clean rejection + structured
                    // log) rather than throwing an unhandled 500 out of the
                    // auth middleware.
                    _log.LogError(ex,
                        "Perpetual system JWT allowlist check failed (DB outage?) — failing closed for jti={Jti}.",
                        jti);
                    return new SystemJwtValidationResult(false, null, "allowlist check unavailable");
                }

                if (!active)
                {
                    _log.LogWarning(
                        "Perpetual system JWT rejected: jti={Jti} not Active in DB allowlist.",
                        jti);
                    return new SystemJwtValidationResult(false, null, "token not active");
                }
            }
        }

        return new SystemJwtValidationResult(true, systemKey, null);
    }

    /// <summary>
    /// Phase S.8d — durable allowlist check for a perpetual token's jti.
    /// True only when a SystemIssuedTokens row exists AND is Active.
    ///
    /// <para>Cached 60s to keep a flood of perpetual-token requests off the
    /// DB. Because the Redis blocklist (checked first) already delivers
    /// instant revoke, this short TTL only delays the durable backstop — the
    /// single scenario it leaves briefly open (a token revoked in the DB
    /// while Redis simultaneously loses its blocklist entry) closes within
    /// the minute. Resolved through a fresh scope because the validator is a
    /// singleton and the repository/DbContext are scoped.</para>
    /// </summary>
    private bool IsPerpetualTokenActive(string jti)
        => _cache.GetOrCreate($"iam:perp-jti:{jti}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISystemIssuedTokenRepository>();
            var row = repo.GetByJtiAsync(jti).GetAwaiter().GetResult();
            return row is not null && row.Status == SystemIssuedTokenStatus.Active;
        });

    public void Dispose() => _rsa.Dispose();
}
