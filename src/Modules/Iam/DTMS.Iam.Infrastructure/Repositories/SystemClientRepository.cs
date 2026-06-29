using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Iam.Infrastructure.Repositories;

public sealed class SystemClientRepository : ISystemClientRepository
{
    private readonly IamDbContext _db;

    public SystemClientRepository(IamDbContext db) => _db = db;

    public Task<SystemClient?> GetByKeyAsync(string key, CancellationToken ct = default)
        => _db.SystemClients.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);

    public async Task<IReadOnlyList<SystemClient>> ListActiveAsync(CancellationToken ct = default)
    {
        var rows = await _db.SystemClients.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Key)
            .ToListAsync(ct);
        return rows;
    }

    public async Task<IReadOnlyList<string>> GetPermissionCodesAsync(
        string systemKey, CancellationToken ct = default)
    {
        var codes = await _db.SystemClientPermissions.AsNoTracking()
            .Where(p => p.SystemKey == systemKey)
            .Select(p => p.PermissionCode)
            .ToListAsync(ct);
        return codes;
    }
}
