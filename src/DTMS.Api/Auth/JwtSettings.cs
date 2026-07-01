namespace DTMS.Api.Auth;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    // RSA public key (PKCS#1 base64) used to validate JWT signatures
    // issued by the External Auth service. DTMS never signs its own
    // *user* tokens — per ADR-014, External Auth owns user identity.
    //
    // Phase 0 (multi-service auth roadmap): External Auth's token has no
    // iss/aud claims, so issuer/audience validation is disabled. Phase 1
    // will reintroduce them once External Auth includes those claims.
    public string PublicKey { get; set; } = string.Empty;

    // ── Phase S.8 — System (M2M) JWT issuance ─────────────────────────────
    // Keypair DTMS uses to sign OAuth client_credentials access tokens for
    // federated source systems (scheme=bearer-jwt). DISTINCT from PublicKey
    // above — that one verifies *user* tokens from External Auth; these sign
    // *system* tokens minted by /oauth/token here.
    //
    // PEM format. PrivateKey may be PKCS#8 ("BEGIN PRIVATE KEY") or PKCS#1
    // ("BEGIN RSA PRIVATE KEY"); PublicKey may be SubjectPublicKeyInfo
    // ("BEGIN PUBLIC KEY") or PKCS#1 ("BEGIN RSA PUBLIC KEY"). Set via env
    // vars Jwt__SystemSigningPrivateKey / Jwt__SystemSigningPublicKey so
    // private material never lands in source control.
    public string SystemSigningPrivateKey { get; set; } = string.Empty;
    public string SystemSigningPublicKey { get; set; } = string.Empty;

    // iss / aud claims stamped onto issued tokens AND required on inbound
    // verification (middleware rejects token whose iss/aud don't match).
    public string SystemTokenIssuer { get; set; } = "dtms";
    public string SystemTokenAudience { get; set; } = "dtms-api";

    // Default lifetime when SystemCredential.AuthConfig doesn't override.
    // 1 hour balances "short blast radius if leaked" vs "partner not
    // hammering /oauth/token". RFC 6749 §4.4 says no refresh token for
    // client_credentials — partner just re-fetches when current expires.
    public int SystemTokenLifetimeSeconds { get; set; } = 3600;

    // Stamped into JWT header so partners (and a future JWKS endpoint) can
    // pick the right key during rotation. Bump when keypair rotates.
    public string SystemTokenKeyId { get; set; } = "dtms-system-v1";
}
