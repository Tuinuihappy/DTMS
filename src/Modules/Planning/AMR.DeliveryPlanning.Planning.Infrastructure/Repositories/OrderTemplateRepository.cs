using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Repositories;

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

    public async Task<IReadOnlyList<OrderTemplate>> ListAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.OrderTemplates.AsQueryable();
        if (!includeInactive)
            query = query.Where(t => t.IsActive);
        return await query.OrderBy(t => t.Name).ToListAsync(cancellationToken);
    }

    public Task AddAsync(OrderTemplate template, CancellationToken cancellationToken = default)
        => _context.OrderTemplates.AddAsync(template, cancellationToken).AsTask();

    public void Update(OrderTemplate template) => _context.OrderTemplates.Update(template);

    public void Remove(OrderTemplate template) => _context.OrderTemplates.Remove(template);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
