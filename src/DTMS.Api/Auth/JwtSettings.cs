namespace DTMS.Api.Auth;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    // RSA public key (PKCS#1 base64) used to validate JWT signatures
    // issued by the External Auth service. DTMS never signs its own
    // tokens — per ADR-014, External Auth owns identity.
    //
    // Phase 0 (multi-service auth roadmap): External Auth's token has no
    // iss/aud claims, so issuer/audience validation is disabled. Phase 1
    // will reintroduce them once External Auth includes those claims.
    public string PublicKey { get; set; } = string.Empty;
}
