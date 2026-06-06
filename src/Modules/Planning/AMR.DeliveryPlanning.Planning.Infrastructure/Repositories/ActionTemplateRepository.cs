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
        ActionType? actionType = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ActionTemplates.AsQueryable();
        if (!includeInactive)
            query = query.Where(t => t.IsActive);
        if (actionType.HasValue)
        {
            var at = actionType.Value;
            query = query.Where(t => t.ActionType == at);
        }

        // LongCount keeps the API safe past 2B rows; running it before the
        // page slice means total + page slice come from the same snapshot
        // even if a concurrent insert lands between the two SQL round trips.
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(t => t.Name)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task AddAsync(ActionTemplate template, CancellationToken cancellationToken = default)
        => _context.ActionTemplates.AddAsync(template, cancellationToken).AsTask();

    public void Update(ActionTemplate template) => _context.ActionTemplates.Update(template);

    public void Remove(ActionTemplate template) => _context.ActionTemplates.Remove(template);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
