using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Iam.Infrastructure.Repositories;

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly IamDbContext _db;

    public PermissionRepository(IamDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> GetPermissionCodesForRoleAsync(
        string role, CancellationToken ct = default)
    {
        return await _db.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.Role == role)
            .Select(rp => rp.PermissionCode)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Permission>> ListAllAsync(CancellationToken ct = default)
    {
        return await _db.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Module)
            .ThenBy(p => p.Code)
            .ToListAsync(ct);
    }

    public Task<Permission?> GetByCodeAsync(string code, CancellationToken ct = default)
        => _db.Permissions.FirstOrDefaultAsync(p => p.Code == code, ct);

    public async Task AddAsync(Permission permission, CancellationToken ct = default)
    {
        _db.Permissions.Add(permission);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Permission permission, CancellationToken ct = default)
    {
        _db.Permissions.Update(permission);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string code, CancellationToken ct = default)
    {
        // Hard delete — cascade through RolePermissions via the FK we
        // haven't added (permission_code has no FK currently). Delete
        // mappings first to keep things tidy.
        await _db.RolePermissions
            .Where(rp => rp.PermissionCode == code)
            .ExecuteDeleteAsync(ct);
        await _db.Permissions
            .Where(p => p.Code == code)
            .ExecuteDeleteAsync(ct);
    }
}
