using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Iam.Infrastructure.Repositories;

public sealed class SystemIssuedTokenRepository : ISystemIssuedTokenRepository
{
    private readonly IamDbContext _db;

    public SystemIssuedTokenRepository(IamDbContext db) => _db = db;

    public async Task AddAsync(SystemIssuedToken token, CancellationToken ct = default)
    {
        _db.SystemIssuedTokens.Add(token);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SystemIssuedToken>> ListBySystemAsync(string systemKey, CancellationToken ct = default)
    {
        return await _db.SystemIssuedTokens
            .AsNoTracking()
            .Where(t => t.SystemKey == systemKey)
            .OrderByDescending(t => t.IssuedAt)
            .ToListAsync(ct);
    }

    public Task<SystemIssuedToken?> GetByJtiAsync(string jti, CancellationToken ct = default)
        => _db.SystemIssuedTokens.FirstOrDefaultAsync(t => t.Jti == jti, ct);

    public async Task UpdateAsync(SystemIssuedToken token, CancellationToken ct = default)
    {
        _db.SystemIssuedTokens.Update(token);
        await _db.SaveChangesAsync(ct);
    }
}
