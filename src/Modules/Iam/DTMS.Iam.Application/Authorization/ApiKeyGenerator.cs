using System.Security.Cryptography;
using System.Text;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Phase S.4 — mints inbound API keys for newly-created SystemClients
/// + their rotations. Plaintext follows a stable shape
/// <c>dtms_{systemKey}_{32-bytes-base64url}</c> so admin can spot
/// which system a leaked key belongs to without ever decoding it.
///
/// <para>The repository stores only <see cref="ApiKey.Sha256Hex"/>;
/// the plaintext is returned to the caller exactly once at creation /
/// rotation time. Same SHA256-hex shape the inbound auth middleware
/// already verifies, so no protocol changes downstream.</para>
/// </summary>
public static class ApiKeyGenerator
{
    public sealed record ApiKey(string Plaintext, string Sha256Hex);

    public static ApiKey Mint(string systemKey)
    {
        if (string.IsNullOrWhiteSpace(systemKey))
            throw new ArgumentException("systemKey required.", nameof(systemKey));

        // 32 bytes = 256 bits of entropy. base64url (no padding) yields
        // 43 chars — wider than RFC 1924 yet still URL-safe so admins
        // can paste into curl without escaping.
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var rand = Base64Url(bytes);

        var plaintext = $"dtms_{systemKey}_{rand}";
        var hash = HashHex(plaintext);
        return new ApiKey(plaintext, hash);
    }

    /// <summary>
    /// Hash an externally-supplied plaintext (e.g. ops paste during
    /// migration from another system) so the admin endpoint can offer
    /// "set key to this value" alongside "generate a new one".
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
        return Convert.ToHexString(hash); // upper-case hex, matches existing rows
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
