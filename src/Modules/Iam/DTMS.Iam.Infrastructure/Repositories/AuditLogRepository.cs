using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Iam.Infrastructure.Repositories;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly IamDbContext _db;

    public AuditLogRepository(IamDbContext db) => _db = db;

    public async Task AppendAsync(PermissionAuditEntry entry, CancellationToken ct = default)
    {
        _db.AuditLog.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<(IReadOnlyList<PermissionAuditEntry> Items, int TotalCount)> QueryAsync(
        string? actorEmployeeId,
        string? role,
        string? action,
        int page,
        int pageSize,
        string? systemKey = null,
        CancellationToken ct = default)
    {
        var query = _db.AuditLog.AsNoTracking().AsQueryable();

        // Details is the compact JSON the writers serialize, so the exact
        // `"systemKey":"oms"` pair (closing quote included — "oms" must not
        // match "oms2") identifies the system. A substring match, sent as a
        // parameter; the table grows a few rows a day, so no column/index.
        if (!string.IsNullOrWhiteSpace(systemKey))
        {
            var needle = $"\"systemKey\":\"{systemKey.Trim()}\"";
            query = query.Where(a => a.Details != null && a.Details.Contains(needle));
        }

        if (!string.IsNullOrWhiteSpace(actorEmployeeId))
            query = query.Where(a => a.ActorEmployeeId == actorEmployeeId);
        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(a => a.Role == role);
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<string>> DistinctActionsAsync(CancellationToken ct = default)
        => await _db.AuditLog.AsNoTracking()
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct);
}
