namespace DTMS.Iam.Application.Repositories;

/// <summary>
/// Read-only lookup for what permissions a role is granted. Phase A
/// has no write surface — mappings are seeded via migrations until
/// Phase B (Admin UI) lands.
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
}
