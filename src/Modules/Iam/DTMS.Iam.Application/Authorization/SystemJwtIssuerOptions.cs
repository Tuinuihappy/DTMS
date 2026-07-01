namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Configuration for <see cref="SystemJwtIssuer"/>. Mapped at startup from
/// the DTMS.Api JwtSettings section — kept separate so the Application
/// layer doesn't depend on the API project (the user-JWT public key lives
/// next to these system-JWT fields in JwtSettings but only matters to the
/// JwtBearer pipeline, not here).
/// </summary>
public sealed class SystemJwtIssuerOptions
{
    /// <summary>RSA private key, PEM-encoded. Accepts PKCS#8
    /// (<c>BEGIN PRIVATE KEY</c>) or PKCS#1 (<c>BEGIN RSA PRIVATE KEY</c>).
    /// Required by <see cref="SystemJwtIssuer"/> — its constructor throws
    /// if empty. May be left empty in environments that only verify
    /// inbound tokens (none today, kept for future hot-spare topologies).</summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>RSA public key, PEM-encoded. SubjectPublicKeyInfo
    /// (<c>BEGIN PUBLIC KEY</c>) or PKCS#1 (<c>BEGIN RSA PUBLIC KEY</c>).
    /// Required by <see cref="SystemJwtValidator"/>. Same logical keypair
    /// as <see cref="PrivateKeyPem"/> — kept as a separate field rather
    /// than derived from the private one so a JWKS-style topology (verifier
    /// without signing material) can drop in without rewriting the
    /// options.</summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary><c>iss</c> claim stamped on every minted token. Inbound
    /// middleware rejects tokens whose iss doesn't match this exact value.</summary>
    public string Issuer { get; set; } = "dtms";

    /// <summary><c>aud</c> claim stamped on every minted token. Inbound
    /// middleware rejects tokens whose aud doesn't match.</summary>
    public string Audience { get; set; } = "dtms-api";

    /// <summary>Default token lifetime when the SystemCredential's AuthConfig
    /// doesn't specify <c>tokenLifetimeSeconds</c>. 1 hour balances "small
    /// blast radius if leaked" against "partner not hammering token endpoint".</summary>
    public int DefaultLifetimeSeconds { get; set; } = 3600;

    /// <summary><c>kid</c> stamped in the JWT header so partners (and a
    /// future /.well-known/jwks.json endpoint) can pick the right key during
    /// rotation. Bump together with the keypair.</summary>
    public string KeyId { get; set; } = "dtms-system-v1";
}
