using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Iam.Infrastructure.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private readonly IamDbContext _db;

    public RoleRepository(IamDbContext db) => _db = db;

    public async Task<IReadOnlyList<Role>> ListAllAsync(CancellationToken ct = default)
    {
        return await _db.Roles
            .AsNoTracking()
            .OrderByDescending(r => r.IsSystem)
            .ThenBy(r => r.Name)
            .ToListAsync(ct);
    }

    public Task<Role?> GetByNameAsync(string name, CancellationToken ct = default)
        => _db.Roles.FirstOrDefaultAsync(r => r.Name == name, ct);

    public async Task AddAsync(Role role, CancellationToken ct = default)
    {
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Role role, CancellationToken ct = default)
    {
        _db.Roles.Update(role);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        // ON DELETE CASCADE on RolePermissions handles mapping cleanup.
        await _db.Roles.Where(r => r.Name == name).ExecuteDeleteAsync(ct);
    }

    public async Task<bool> GrantPermissionAsync(string role, string permissionCode, CancellationToken ct = default)
    {
        var exists = await _db.RolePermissions
            .AnyAsync(rp => rp.Role == role && rp.PermissionCode == permissionCode, ct);
        if (exists) return false;

        _db.RolePermissions.Add(new RolePermission(role, permissionCode));
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RevokePermissionAsync(string role, string permissionCode, CancellationToken ct = default)
    {
        var deleted = await _db.RolePermissions
            .Where(rp => rp.Role == role && rp.PermissionCode == permissionCode)
            .ExecuteDeleteAsync(ct);
        return deleted > 0;
    }
}
