using System.Security.Cryptography;
using DTMS.Iam.Application.Security;
using Microsoft.AspNetCore.DataProtection;

namespace DTMS.Iam.Infrastructure.Security;

/// <summary>
/// <see cref="ICallbackTokenProtector"/> backed by ASP.NET Data Protection.
/// The key ring lives on the dp-keys volume shared by api / outbox-worker /
/// migrator (DataProtection__KeyPath) so ciphertext written by one process
/// is readable by the others.
/// </summary>
public sealed class CallbackTokenProtector : ICallbackTokenProtector
{
    public const string Purpose = "DTMS.Iam.CallbackAuthConfig";

    /// <summary>
    /// Base64Url encoding of the magic header (0x09F0C9F0) that prefixes
    /// every Data Protection payload — the plaintext/ciphertext
    /// discriminator. Callback configs are JSON objects (start with '{')
    /// so the two can never collide.
    /// </summary>
    public const string CiphertextPrefix = "CfDJ8";

    private readonly IDataProtector _protector;

    public CallbackTokenProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose);

    public string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (value.StartsWith(CiphertextPrefix, StringComparison.Ordinal))
            return value; // already ciphertext — idempotent
        return _protector.Protect(value);
    }

    public string? TryUnprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (!value.StartsWith(CiphertextPrefix, StringComparison.Ordinal))
            return value; // legacy plaintext row (pre-backfill) — pass through
        try
        {
            return _protector.Unprotect(value);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Stored callback token cannot be decrypted — the Data Protection " +
                "key ring (dp-keys volume) no longer holds the key that encrypted " +
                "it. Re-save the token via the admin Configure UI (PUT /callback).",
                ex);
        }
    }
}
