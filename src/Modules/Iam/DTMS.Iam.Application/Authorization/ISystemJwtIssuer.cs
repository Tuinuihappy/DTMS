namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Phase S.8 — mints OAuth client_credentials access tokens for federated
/// source systems whose <c>SystemCredential.AuthScheme = "bearer-jwt"</c>.
/// Lives behind an interface so the token endpoint can be tested without
/// real RSA material and so a future JWKS rotation strategy (multiple
/// active signing keys) can swap the implementation without touching
/// callers.
/// </summary>
public interface ISystemJwtIssuer
{
    /// <param name="systemKey">Slug of the SystemClient the token is for —
    /// becomes the <c>sub</c> claim as <c>system:{key}</c>.</param>
    /// <param name="lifetimeSecondsOverride">Per-credential override (read
    /// from AuthConfig.tokenLifetimeSeconds). Falls back to options default
    /// when null. Ignored when <paramref name="neverExpires"/> is true.</param>
    /// <param name="neverExpires">Phase S.8d — mint a perpetual token with
    /// NO <c>exp</c> claim. Admin-issue path only; the caller MUST persist a
    /// SystemIssuedTokens row because the durable DB allowlist is the sole
    /// kill switch for a token that never naturally expires.</param>
    IssuedSystemToken Issue(string systemKey, int? lifetimeSecondsOverride = null, bool neverExpires = false);
}

/// <summary>
/// Result of a successful <see cref="ISystemJwtIssuer.Issue"/>. The
/// <see cref="ExpiresInSeconds"/> field is what RFC 6749 §5.1 calls
/// <c>expires_in</c>; the absolute <see cref="ExpiresAt"/> is exposed too
/// for callers that log token issuance. <see cref="Jti"/> is the JWT
/// ID stamped in the payload — the admin issue endpoint records it in
/// <c>iam.SystemIssuedTokens</c> for the revocation list.
/// </summary>
public sealed record IssuedSystemToken(
    string AccessToken,
    // Null for a perpetual (never-expires) token — it carries no exp claim,
    // so there is no expires_in to report per RFC 6749 §5.1.
    int? ExpiresInSeconds,
    DateTime? ExpiresAt,
    string Jti);
