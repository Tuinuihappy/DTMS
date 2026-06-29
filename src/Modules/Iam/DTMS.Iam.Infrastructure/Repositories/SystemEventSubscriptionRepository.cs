using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Iam.Infrastructure.Repositories;

public sealed class SystemEventSubscriptionRepository : ISystemEventSubscriptionRepository
{
    private readonly IamDbContext _db;

    public SystemEventSubscriptionRepository(IamDbContext db) => _db = db;

    public Task<SystemEventSubscription?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.SystemEventSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<SystemEventSubscription?> GetAsync(string systemKey, string eventType, CancellationToken ct = default)
        => _db.SystemEventSubscriptions
            .FirstOrDefaultAsync(s => s.SystemKey == systemKey && s.EventType == eventType, ct);

    public async Task<IReadOnlyList<SystemEventSubscription>> ListBySystemAsync(
        string systemKey, CancellationToken ct = default)
    {
        var rows = await _db.SystemEventSubscriptions.AsNoTracking()
            .Where(s => s.SystemKey == systemKey)
            .OrderBy(s => s.EventType)
            .ToListAsync(ct);
        return rows;
    }

    public async Task<IReadOnlyList<SystemEventSubscription>> ListEnabledByEventTypeAsync(
        string eventType, CancellationToken ct = default)
    {
        // Hot path — AsNoTracking + minimal projection target on the read
        // side. The cache layer above is what keeps the DB out of the
        // request hot path; this method runs on cache miss only.
        var rows = await _db.SystemEventSubscriptions.AsNoTracking()
            .Where(s => s.EventType == eventType && s.Enabled)
            .OrderBy(s => s.SystemKey)
            .ToListAsync(ct);
        return rows;
    }

    public async Task AddAsync(SystemEventSubscription subscription, CancellationToken ct = default)
    {
        _db.SystemEventSubscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SystemEventSubscription subscription, CancellationToken ct = default)
    {
        _db.SystemEventSubscriptions.Update(subscription);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(SystemEventSubscription subscription, CancellationToken ct = default)
    {
        _db.SystemEventSubscriptions.Remove(subscription);
        await _db.SaveChangesAsync(ct);
    }
}
