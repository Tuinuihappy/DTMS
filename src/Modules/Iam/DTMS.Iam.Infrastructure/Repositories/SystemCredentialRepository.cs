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
