using System.Security.Cryptography;
using System.Text;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Phase S.8 — mints OAuth client_credentials secrets for SystemClients
/// that use <c>scheme=bearer-jwt</c>. Mirrors <see cref="ApiKeyGenerator"/>
/// in shape (return plaintext once, store hash forever) so admin + audit
/// flows treat both credential kinds identically. The plaintext shape is
/// <c>dtms_cs_{systemKey}_{32-bytes-base64url}</c>:
/// <list type="bullet">
///   <item><c>dtms_cs_</c> prefix lets an admin who finds a leaked string
///   tell at a glance whether it's an inbound API key
///   (<c>dtms_</c>) or a client secret (<c>dtms_cs_</c>) — they unlock
///   different surfaces (direct API vs token endpoint).</item>
///   <item>System slug embedded so a leaked secret reveals which integration
///   to rotate without decoding anything.</item>
///   <item>43-char base64url body = 256 bits entropy, URL-safe so partners
///   can curl <c>/oauth/token</c> without escape gymnastics.</item>
/// </list>
/// </summary>
public static class ClientSecretGenerator
{
    public sealed record ClientSecret(string Plaintext, string Sha256Hex);

    public static ClientSecret Mint(string systemKey)
    {
        if (string.IsNullOrWhiteSpace(systemKey))
            throw new ArgumentException("systemKey required.", nameof(systemKey));

        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var rand = Base64Url(bytes);

        var plaintext = $"dtms_cs_{systemKey}_{rand}";
        var hash = HashHex(plaintext);
        return new ClientSecret(plaintext, hash);
    }

    /// <summary>
    /// Hash a caller-supplied plaintext. Used by the rotate endpoint when an
    /// admin pastes a secret minted elsewhere (e.g. during migration from a
    /// legacy system) rather than asking us to generate one.
    /// </summary>
    public static string HashPlaintext(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            throw new ArgumentException("plaintext required.", nameof(plaintext));
        return HashHex(plaintext);
    }

    private static string HashHex(string plaintext)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(plaintext), hash);
        return Convert.ToHexString(hash);
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
