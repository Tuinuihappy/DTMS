using DTMS.Iam.Domain.Entities;

namespace DTMS.Iam.Application.Repositories;

/// <summary>
/// CRUD for the role catalog + management of the role-permission mapping.
/// System roles (IsSystem=true) reject Delete — only their permission
/// mapping can be edited.
/// </summary>
public interface IRoleRepository
{
    Task<IReadOnlyList<Role>> ListAllAsync(CancellationToken ct = default);

    Task<Role?> GetByNameAsync(string name, CancellationToken ct = default);

    Task AddAsync(Role role, CancellationToken ct = default);

    Task UpdateAsync(Role role, CancellationToken ct = default);

    Task DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Adds a (role, permission) row if it doesn't exist. Returns true on
    /// insert, false if the mapping already existed.
    /// </summary>
    Task<bool> GrantPermissionAsync(string role, string permissionCode, CancellationToken ct = default);

    /// <summary>
    /// Removes a (role, permission) row. Returns true on delete, false if
    /// the mapping didn't exist.
    /// </summary>
    Task<bool> RevokePermissionAsync(string role, string permissionCode, CancellationToken ct = default);
}
