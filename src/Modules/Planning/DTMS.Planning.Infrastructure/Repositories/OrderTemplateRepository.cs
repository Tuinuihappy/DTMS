using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Repositories;
using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Planning.Infrastructure.Repositories;

public class OrderTemplateRepository : IOrderTemplateRepository
{
    private readonly PlanningDbContext _context;

    public OrderTemplateRepository(PlanningDbContext context)
    {
        _context = context;
    }

    public Task<OrderTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.OrderTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<OrderTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = (name ?? string.Empty).Trim();
        return _context.OrderTemplates
            .FirstOrDefaultAsync(t => t.Name.ToLower() == normalized.ToLower(), cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = (name ?? string.Empty).Trim();
        var query = _context.OrderTemplates.Where(t => t.Name.ToLower() == normalized.ToLower());
        if (excludeId.HasValue)
            query = query.Where(t => t.Id != excludeId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<OrderTemplate> Items, long Total)> ListPagedAsync(
        int page,
        int size,
        bool includeInactive = false,
        string? sortBy = null,
        bool sortDescending = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.OrderTemplates.AsQueryable();
        if (!includeInactive)
            query = query.Where(t => t.IsActive);

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

    // Maps the frontend sort-column tokens to LINQ ordering. Unknown
    // values fall back to Name asc so a forgetful client sees the same
    // deterministic order the catalog had before sortBy was introduced.
    private static IOrderedQueryable<OrderTemplate> ApplyOrdering(
        IQueryable<OrderTemplate> query, string? sortBy, bool descending)
    {
        return sortBy switch
        {
            "priority" => descending
                ? query.OrderByDescending(t => t.Priority).ThenBy(t => t.Name)
                : query.OrderBy(t => t.Priority).ThenBy(t => t.Name),
            "isActive" => descending
                ? query.OrderByDescending(t => t.IsActive).ThenBy(t => t.Name)
                : query.OrderBy(t => t.IsActive).ThenBy(t => t.Name),
            "modifiedAt" => descending
                // ModifiedAt is null on never-edited rows; fall back to
                // CreatedAt so the column reads as "last touched" rather
                // than bucketing every fresh row to the bottom.
                ? query.OrderByDescending(t => t.ModifiedAt ?? t.CreatedAt)
                : query.OrderBy(t => t.ModifiedAt ?? t.CreatedAt),
            "createdAt" => descending
                ? query.OrderByDescending(t => t.CreatedAt)
                : query.OrderBy(t => t.CreatedAt),
            _ => descending ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
        };
    }

    public Task<OrderTemplate?> FindByRouteAsync(
        Guid pickupStationId,
        Guid dropStationId,
        CancellationToken cancellationToken = default)
    {
        return _context.OrderTemplates
            .Where(t => t.IsActive
                     && t.PickupStationId == pickupStationId
                     && t.DropStationId == dropStationId)
            .OrderBy(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task AddAsync(OrderTemplate template, CancellationToken cancellationToken = default)
        => _context.OrderTemplates.AddAsync(template, cancellationToken).AsTask();

    public void Update(OrderTemplate template) => _context.OrderTemplates.Update(template);

    public void Remove(OrderTemplate template) => _context.OrderTemplates.Remove(template);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
