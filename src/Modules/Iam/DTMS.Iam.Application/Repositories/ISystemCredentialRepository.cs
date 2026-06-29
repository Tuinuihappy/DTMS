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

    // ── Phase S.4 admin CRUD ────────────────────────────────────────────

    Task AddAsync(SystemCredential credential, CancellationToken ct = default);
    Task UpdateAsync(SystemCredential credential, CancellationToken ct = default);
}
