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

    // ── Phase S.4 admin CRUD ────────────────────────────────────────────

    public async Task<IReadOnlyList<SystemClient>> ListAllAsync(CancellationToken ct = default)
        => await _db.SystemClients.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct);

    public async Task AddWithPermissionsAsync(
        SystemClient client,
        IReadOnlyList<string> permissionCodes,
        string grantedBy,
        CancellationToken ct = default)
    {
        // Wrap in execution-strategy because the IamDbContext is
        // EnableRetryOnFailure-equipped — an explicit transaction
        // outside the strategy would throw.
        //
        // Two SaveChanges inside one tx: SystemClient is persisted +
        // committed to its row first so the permission rows' FK to
        // SystemClients(Key) can resolve. We don't model the
        // SystemClient → SystemClientPermissions relationship via
        // navigation properties (S.2 design — they're separate
        // aggregates), so EF's change tracker can't order the inserts
        // by itself.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            _db.SystemClients.Add(client);
            await _db.SaveChangesAsync(ct);

            foreach (var code in permissionCodes.Distinct(StringComparer.Ordinal))
            {
                _db.SystemClientPermissions.Add(
                    new Domain.Entities.SystemClientPermission(client.Key, code, grantedBy));
            }
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    public async Task UpdateAsync(SystemClient client, CancellationToken ct = default)
    {
        _db.SystemClients.Update(client);
        await _db.SaveChangesAsync(ct);
    }
}
