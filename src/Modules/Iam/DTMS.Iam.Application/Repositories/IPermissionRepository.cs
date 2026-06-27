using DTMS.Iam.Domain.Entities;

namespace DTMS.Iam.Application.Repositories;

/// <summary>
/// Read + admin write surface for the permission catalog. The hot-path
/// <see cref="GetPermissionCodesForRoleAsync"/> backs PermissionClaimsTransformer
/// (cached); the CRUD methods back the Phase B admin UI and write straight
/// through (the cache flushes naturally on TTL within 5 minutes).
/// </summary>
public interface IPermissionRepository
{
    /// <summary>
    /// Returns the raw permission codes granted to <paramref name="role"/>,
    /// including wildcards (e.g. <c>dtms:*</c>). The caller is responsible
    /// for expanding wildcards against requested permission codes.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionCodesForRoleAsync(
        string role, CancellationToken ct = default);

    Task<IReadOnlyList<Permission>> ListAllAsync(CancellationToken ct = default);

    Task<Permission?> GetByCodeAsync(string code, CancellationToken ct = default);

    Task AddAsync(Permission permission, CancellationToken ct = default);

    Task UpdateAsync(Permission permission, CancellationToken ct = default);

    Task DeleteAsync(string code, CancellationToken ct = default);
}
