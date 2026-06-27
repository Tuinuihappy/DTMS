using DTMS.Iam.Application.Repositories;
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
}
