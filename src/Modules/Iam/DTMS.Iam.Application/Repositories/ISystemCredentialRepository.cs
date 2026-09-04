using DTMS.Iam.Domain.Entities;

namespace DTMS.Iam.Application.Repositories;

/// <summary>
/// Persistence for <see cref="SystemCredential"/>. The cached
/// credential reader (S.2 §7) consults the read method on cache miss;
/// Phase S.4 adds the write side for admin rotate / callback config.
/// One interface per aggregate — see <see cref="ISystemClientRepository"/>
/// docs for the rationale.
/// </summary>
public interface ISystemCredentialRepository
{
    Task<SystemCredential?> GetBySystemKeyAsync(string systemKey, CancellationToken ct = default);

    /// <summary>System keys whose row has a <c>TokenRefreshConfig</c> set — the
    /// candidate set the auto-refresh loop sweeps. Keys only (not full entities)
    /// so the encrypted config is not decrypted just to enumerate; the refresher
    /// reloads each row fresh under its lock anyway.</summary>
    Task<IReadOnlyList<string>> ListKeysWithTokenRefreshAsync(CancellationToken ct = default);

    /// <summary>Keys of the systems that borrow their token from
    /// <paramref name="ownerKey"/>. Used to explain the link in the admin view
    /// and to refuse deleting or un-owning a row others still depend on.</summary>
    Task<IReadOnlyList<string>> ListBorrowerKeysAsync(string ownerKey, CancellationToken ct = default);

    /// <summary>Every row that mints its own token, as (key, config) pairs.
    /// The caller compares mint identities to stop two systems being pointed at
    /// the same account — on an auth service that keeps one live token per
    /// account, that makes them silently invalidate each other.</summary>
    Task<IReadOnlyList<(string SystemKey, string TokenRefreshConfig)>> ListTokenRefreshConfigsAsync(
        CancellationToken ct = default);

    // ── Phase S.4 admin CRUD ────────────────────────────────────────────

    Task AddAsync(SystemCredential credential, CancellationToken ct = default);
    Task UpdateAsync(SystemCredential credential, CancellationToken ct = default);
}
