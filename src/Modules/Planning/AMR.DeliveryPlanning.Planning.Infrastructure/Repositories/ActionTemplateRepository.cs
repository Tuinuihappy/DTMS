using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Repositories;

public class ActionTemplateRepository : IActionTemplateRepository
{
    private readonly PlanningDbContext _context;

    public ActionTemplateRepository(PlanningDbContext context)
    {
        _context = context;
    }

    public Task<ActionTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.ActionTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<ActionTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        // PostgreSQL `ILIKE` would be ideal, but we want LINQ portability and
        // we already enforce trim on the entity side. EF.Functions.ILike works
        // on Npgsql; the lower() comparison is the portable fallback.
        var normalized = (name ?? string.Empty).Trim();
        return _context.ActionTemplates
            .FirstOrDefaultAsync(t => t.Name.ToLower() == normalized.ToLower(), cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = (name ?? string.Empty).Trim();
        var query = _context.ActionTemplates.Where(t => t.Name.ToLower() == normalized.ToLower());
        if (excludeId.HasValue)
            query = query.Where(t => t.Id != excludeId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<ActionTemplate> Items, long Total)> ListPagedAsync(
        int page,
        int size,
        bool includeInactive = false,
        ActionCategory? actionCategory = null,
        string? search = null,
        string? sortBy = null,
        bool sortDescending = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ActionTemplates.AsQueryable();
        if (!includeInactive)
            query = query.Where(t => t.IsActive);
        if (actionCategory.HasValue)
        {
            var cat = actionCategory.Value;
            query = query.Where(t => t.ActionCategory == cat);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive LIKE on Name. EF.Functions.ILike is Npgsql-only
            // but Planning targets Postgres in prod + integration tests — the
            // lower()+Contains fallback used elsewhere translates to the same
            // ILIKE on Npgsql so either form is fine; ILike makes intent clear.
            var needle = $"%{search.Trim()}%";
            query = query.Where(t => EF.Functions.ILike(t.Name, needle));
        }

        // LongCount keeps the API safe past 2B rows; running it before the
        // page slice means total + page slice come from the same snapshot
        // even if a concurrent insert lands between the two SQL round trips.
        var total = await query.LongCountAsync(cancellationToken);
        var ordered = ApplyOrdering(query, sortBy, sortDescending);
        var items = await ordered
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    // Maps the frontend sort-column tokens to LINQ ordering. Unknown values
    // fall back to Name asc — matches the previous default and keeps the
    // result deterministic when the client forgets to send sortBy.
    private static IOrderedQueryable<ActionTemplate> ApplyOrdering(
        IQueryable<ActionTemplate> query, string? sortBy, bool descending)
    {
        return sortBy switch
        {
            "actionCategory" => descending
                ? query.OrderByDescending(t => t.ActionCategory).ThenBy(t => t.Name)
                : query.OrderBy(t => t.ActionCategory).ThenBy(t => t.Name),
            "isActive" => descending
                ? query.OrderByDescending(t => t.IsActive).ThenBy(t => t.Name)
                : query.OrderBy(t => t.IsActive).ThenBy(t => t.Name),
            "modifiedAt" => descending
                // ModifiedAt is null on never-edited rows; fall back to
                // CreatedAt so the column reads as "last touched" rather than
                // bucketing every fresh row to the bottom of the list.
                ? query.OrderByDescending(t => t.ModifiedAt ?? t.CreatedAt)
                : query.OrderBy(t => t.ModifiedAt ?? t.CreatedAt),
            _ => descending ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
        };
    }

    public async Task<(int Total, int Active, int Std, int Act)> GetStatsAsync(
        CancellationToken cancellationToken = default)
    {
        // Single round trip — GROUP BY ActionCategory + IsActive returns the
        // four counters in one query rather than four COUNT(*) passes.
        var rows = await _context.ActionTemplates
            .GroupBy(t => new { t.ActionCategory, t.IsActive })
            .Select(g => new { g.Key.ActionCategory, g.Key.IsActive, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var total = rows.Sum(r => r.Count);
        var active = rows.Where(r => r.IsActive).Sum(r => r.Count);
        var std = rows.Where(r => r.ActionCategory == ActionCategory.Std).Sum(r => r.Count);
        var act = rows.Where(r => r.ActionCategory == ActionCategory.Act).Sum(r => r.Count);
        return (total, active, std, act);
    }

    public Task AddAsync(ActionTemplate template, CancellationToken cancellationToken = default)
        => _context.ActionTemplates.AddAsync(template, cancellationToken).AsTask();

    public void Update(ActionTemplate template) => _context.ActionTemplates.Update(template);

    public void Remove(ActionTemplate template) => _context.ActionTemplates.Remove(template);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
