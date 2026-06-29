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
}
