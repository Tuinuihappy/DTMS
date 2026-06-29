using DTMS.Iam.Domain.Entities;

namespace DTMS.Iam.Application.Repositories;

/// <summary>
/// Read-side access to <see cref="SystemCredential"/>. The cached
/// credential reader (S.2 §7) consults this on cache miss; admin
/// rotate / set-callback writes go through a separate write
/// repository in Phase S.4.
/// </summary>
public interface ISystemCredentialRepository
{
    Task<SystemCredential?> GetBySystemKeyAsync(string systemKey, CancellationToken ct = default);
}
