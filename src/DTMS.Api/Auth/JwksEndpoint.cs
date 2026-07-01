using System.Security.Cryptography;
using System.Text.Json.Serialization;
using DTMS.Iam.Application.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DTMS.Api.Auth;

/// <summary>
/// Phase S.8d — RFC 7517 JSON Web Key Set endpoint. Publishes the DTMS
/// system-JWT signing key's PUBLIC half so partners (or downstream
/// gateways / auth proxies) can verify the signature of any
/// <c>Authorization: Bearer &lt;jwt&gt;</c> DTMS issues without embedding
/// the raw PEM in their config.
///
/// <para><b>Public info only.</b> The endpoint exposes only the modulus
/// (n) and public exponent (e) of the RSA public key — this is what
/// every JWKS in the wild does (Google, Auth0, etc.). The private key
/// stays in DTMS memory + .env; there's nothing sensitive on the wire.</para>
///
/// <para><b>Discovery convention.</b> URL is
/// <c>GET /.well-known/jwks.json</c> per RFC 8615 §3 well-known URI
/// suffix. Anonymous access — the JWKS itself is public metadata.</para>
///
/// <para><b>Kid pinning.</b> The <c>kid</c> field matches the one in
/// SystemJwtIssuerOptions.KeyId (also stamped in every issued JWT's
/// header). Partners that want to be pedantic can match kid → JWK
/// entry; DTMS's own validator uses IssuerSigningKeyResolver and
/// ignores kid, but partners doing their own verification typically
/// key on it.</para>
/// </summary>
public static class JwksEndpoint
{
    public const string Path = "/.well-known/jwks.json";

    public static IEndpointRouteBuilder MapJwksEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, (HttpContext ctx, IOptions<SystemJwtIssuerOptions> opts) =>
        {
            var o = opts.Value;
            if (string.IsNullOrWhiteSpace(o.PublicKeyPem))
                return Results.NotFound(new { error = "System JWT signing key not configured." });

            using var rsa = RSA.Create();
            rsa.ImportFromPem(o.PublicKeyPem);
            var parameters = rsa.ExportParameters(includePrivateParameters: false);

            var jwk = new Jwk(
                Kty: "RSA",
                Use: "sig",
                Kid: o.KeyId,
                Alg: SecurityAlgorithms.RsaSha256,
                // JWK numeric parameters are base64url-encoded big-endian
                // byte strings with the leading zero byte stripped. RSA
                // .NET already returns them minimally-encoded; base64url
                // = base64 with -/_ swaps and no padding.
                N: Base64UrlEncoder.Encode(parameters.Modulus!),
                E: Base64UrlEncoder.Encode(parameters.Exponent!));

            // Cache aggressively at the edge — the JWKS only rotates when
            // the keypair does (bumping KeyId), so 1 hour lets CDNs +
            // partner caches absorb the load without partners feeling
            // stale keys. Header set directly instead of ResponseCache
            // attribute so we don't drag in the MVC package for one line.
            ctx.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Ok(new JwkSet(new[] { jwk }));
        })
        .AllowAnonymous()
        .WithTags("OAuth")
        .WithSummary("JSON Web Key Set (RFC 7517) — DTMS system-JWT public key");

        return app;
    }

    /// <summary>RFC 7517 §4 JSON Web Key. Public so OpenAPI can serialize.</summary>
    public sealed record Jwk(
        [property: JsonPropertyName("kty")] string Kty,
        [property: JsonPropertyName("use")] string Use,
        [property: JsonPropertyName("kid")] string Kid,
        [property: JsonPropertyName("alg")] string Alg,
        [property: JsonPropertyName("n")]   string N,
        [property: JsonPropertyName("e")]   string E);

    public sealed record JwkSet(
        [property: JsonPropertyName("keys")] IReadOnlyList<Jwk> Keys);
}
