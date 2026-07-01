using System.Security.Cryptography;
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
    private readonly ILogger<SystemJwtValidator> _log;

    public SystemJwtValidator(
        IOptions<SystemJwtIssuerOptions> opts,
        ISystemJwtRevocationList revocationList,
        ILogger<SystemJwtValidator> log)
    {
        _revocationList = revocationList;
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

        // Phase S.8c — revocation list check (fail-close on Redis outage).
        // Any exception from Redis is treated as "reject the request" —
        // the alternative (fail-open) would let a revoked token slip
        // through during a Redis blip, defeating the point of revocation.
        // GetAwaiter().GetResult() is used because the caller is a sync
        // middleware branch; Redis KeyExistsAsync is a single round-trip
        // (~1-2ms typical) so blocking is acceptable.
        if (result.Claims.TryGetValue("jti", out var jtiObj) && jtiObj is string jti)
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
        }
        // No jti on the token = OAuth-issued short-lived (they still have
        // jti — this branch is defensive for future non-jti tokens).

        return new SystemJwtValidationResult(true, systemKey, null);
    }

    public void Dispose() => _rsa.Dispose();
}
