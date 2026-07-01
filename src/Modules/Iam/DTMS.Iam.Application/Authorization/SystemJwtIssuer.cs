using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Phase S.8 — RS256 implementation of <see cref="ISystemJwtIssuer"/>.
/// Singleton: the RSA key + SigningCredentials are built once at
/// construction so per-token issuance is just a header+payload+sign call.
///
/// <para><b>Modern handler.</b> Uses <see cref="JsonWebTokenHandler"/>
/// rather than the legacy <c>JwtSecurityTokenHandler</c> — faster, less
/// allocation, and the recommended path on .NET 8+.</para>
///
/// <para><b>Claims shape.</b> Only the OAuth-mandated subset plus
/// <c>jti</c>:
/// <list type="bullet">
///   <item><c>iss</c> + <c>aud</c> — from options, validated at inbound</item>
///   <item><c>sub</c> = <c>system:{key}</c> — matches the URL segment so the
///   middleware can refuse a token presented at the wrong /source/{x}</item>
///   <item><c>iat</c>, <c>nbf</c>, <c>exp</c> — lifetime window</item>
///   <item><c>jti</c> — unique id, enables future revocation list</item>
/// </list>
/// Permission codes are deliberately NOT embedded: they're looked up from
/// the DB per-request so a permission revoke takes effect immediately
/// rather than waiting for the token to expire.</para>
/// </summary>
public sealed class SystemJwtIssuer : ISystemJwtIssuer, IDisposable
{
    private readonly SystemJwtIssuerOptions _opts;
    private readonly RSA _rsa;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    public SystemJwtIssuer(IOptions<SystemJwtIssuerOptions> opts)
    {
        _opts = opts.Value;
        if (string.IsNullOrWhiteSpace(_opts.PrivateKeyPem))
            throw new InvalidOperationException(
                "SystemJwtIssuerOptions.PrivateKeyPem is empty. " +
                "Set Jwt__SystemSigningPrivateKey env var with a PEM-encoded RSA private key.");

        _rsa = RSA.Create();
        try
        {
            _rsa.ImportFromPem(_opts.PrivateKeyPem);
        }
        catch (Exception ex)
        {
            _rsa.Dispose();
            throw new InvalidOperationException(
                "Failed to parse SystemJwtIssuerOptions.PrivateKeyPem as a PEM-encoded RSA key. " +
                "Expected PKCS#8 ('BEGIN PRIVATE KEY') or PKCS#1 ('BEGIN RSA PRIVATE KEY').", ex);
        }

        var key = new RsaSecurityKey(_rsa) { KeyId = _opts.KeyId };
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
    }

    public IssuedSystemToken Issue(string systemKey, int? lifetimeSecondsOverride = null)
    {
        if (string.IsNullOrWhiteSpace(systemKey))
            throw new ArgumentException("systemKey required.", nameof(systemKey));

        var lifetime = lifetimeSecondsOverride ?? _opts.DefaultLifetimeSeconds;
        if (lifetime <= 0)
            throw new ArgumentOutOfRangeException(nameof(lifetimeSecondsOverride),
                "Token lifetime must be positive.");

        var now = DateTime.UtcNow;
        var exp = now.AddSeconds(lifetime);
        var jti = Guid.NewGuid().ToString("N");

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _opts.Issuer,
            Audience = _opts.Audience,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, $"system:{systemKey}"),
                new Claim(JwtRegisteredClaimNames.Jti, jti),
            ]),
            IssuedAt = now,
            NotBefore = now,
            Expires = exp,
            SigningCredentials = _signingCredentials,
        };

        var token = _handler.CreateToken(descriptor);
        // Return jti so the caller (admin issue endpoint / OAuth token
        // endpoint) can decide whether to persist it (admin-issued: yes,
        // for revocation list; OAuth-issued: no, short lifetime + high
        // volume makes per-mint DB writes prohibitive).
        return new IssuedSystemToken(token, lifetime, exp, jti);
    }

    public void Dispose() => _rsa.Dispose();
}
