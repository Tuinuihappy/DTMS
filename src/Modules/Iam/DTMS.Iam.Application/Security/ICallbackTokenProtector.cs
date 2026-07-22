using System.Diagnostics.CodeAnalysis;

namespace DTMS.Iam.Application.Security;

/// <summary>
/// Encrypt-at-rest boundary for <c>SystemCredentials.CallbackAuthConfig</c>
/// (the outbound callback bearer token). Everything that persists the value
/// outside process memory — the EF column converter and the credential
/// cache — routes through this so Postgres, its backups, and Redis only
/// ever see ciphertext.
/// </summary>
/// <remarks>
/// The scheme relies on the payload being a JSON object (starts with
/// <c>{</c>) while Data Protection ciphertext always starts with
/// <c>CfDJ8</c> — that prefix is the discriminator that makes both
/// methods idempotent and keeps legacy plaintext rows readable before
/// the startup backfill converts them. Do not reuse this protector for
/// values that are not JSON-shaped.
/// </remarks>
public interface ICallbackTokenProtector
{
    /// <summary>
    /// Plaintext → ciphertext. Pass-through for null/empty and for values
    /// that are already ciphertext (safe to call twice).
    /// </summary>
    [return: NotNullIfNotNull(nameof(value))]
    string? Protect(string? value);

    /// <summary>
    /// Ciphertext → plaintext. Pass-through for null/empty and for legacy
    /// plaintext values. Throws <see cref="InvalidOperationException"/>
    /// with a recovery hint when the key ring no longer holds the key
    /// that produced the ciphertext.
    /// </summary>
    [return: NotNullIfNotNull(nameof(value))]
    string? TryUnprotect(string? value);
}
