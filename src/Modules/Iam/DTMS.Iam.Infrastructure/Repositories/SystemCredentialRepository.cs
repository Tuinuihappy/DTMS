using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Iam.Infrastructure.Repositories;

public sealed class SystemCredentialRepository : ISystemCredentialRepository
{
    private readonly IamDbContext _db;

    public SystemCredentialRepository(IamDbContext db) => _db = db;

    public Task<SystemCredential?> GetBySystemKeyAsync(string systemKey, CancellationToken ct = default)
        => _db.SystemCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SystemKey == systemKey, ct);

    public async Task<IReadOnlyList<string>> ListKeysWithTokenRefreshAsync(CancellationToken ct = default)
        => await _db.SystemCredentials.AsNoTracking()
            .Where(c => c.TokenRefreshConfig != null)
            .Select(c => c.SystemKey)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListBorrowerKeysAsync(
        string ownerKey, CancellationToken ct = default)
        => await _db.SystemCredentials.AsNoTracking()
            .Where(c => c.TokenSourceKey == ownerKey)
            .Select(c => c.SystemKey)
            .OrderBy(k => k)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<(string SystemKey, string TokenRefreshConfig)>>
        ListTokenRefreshConfigsAsync(CancellationToken ct = default)
    {
        // Materialise before projecting to the tuple: the config goes through
        // the decrypting value converter, which EF cannot translate into SQL.
        var rows = await _db.SystemCredentials.AsNoTracking()
            .Where(c => c.TokenRefreshConfig != null)
            .Select(c => new { c.SystemKey, c.TokenRefreshConfig })
            .ToListAsync(ct);

        return rows.Select(r => (r.SystemKey, r.TokenRefreshConfig!)).ToList();
    }

    // ── Phase S.4 admin CRUD ────────────────────────────────────────────

    public async Task AddAsync(SystemCredential credential, CancellationToken ct = default)
    {
        _db.SystemCredentials.Add(credential);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SystemCredential credential, CancellationToken ct = default)
    {
        _db.SystemCredentials.Update(credential);
        await _db.SaveChangesAsync(ct);
    }
}
