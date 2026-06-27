namespace DTMS.Api.Auth;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    // RSA public key (PKCS#1 base64) used to validate JWT signatures
    // issued by the External Auth service. DTMS never signs its own
    // tokens — per ADR-014, External Auth owns identity.
    public string PublicKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}
