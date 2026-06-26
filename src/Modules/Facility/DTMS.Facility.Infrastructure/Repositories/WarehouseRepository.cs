using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.Repositories;
using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Facility.Infrastructure.Repositories;

/// <summary>
/// Concrete EF impl for <see cref="IWarehouseRepository"/>. Follows the
/// existing MapRepository pattern (DbContext injection, EF Include where
/// owned navigations need eager loading, AsNoTracking on read-only paths
/// to avoid change-tracker pressure on the hot order-validation flow).
///
/// EF owned-entity caveat: the value objects (Location, Address, Hours,
/// PrimaryContact) are loaded automatically with the parent — no Include
/// needed. They're in the same row so the query is a single SELECT.
/// </summary>
public class WarehouseRepository : IWarehouseRepository
{
    private readonly FacilityDbContext _dbContext;

    public WarehouseRepository(FacilityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Warehouses
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public Task<Warehouse?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult<Warehouse?>(null);

        // EF.Functions.ILike for case-insensitive Postgres match — keeps
        // the "WH-bkk-01" vs "WH-BKK-01" forgiveness that operator UX
        // depends on (operators don't think in case).
        return _dbContext.Warehouses
            .FirstOrDefaultAsync(w => EF.Functions.ILike(w.Code, code), cancellationToken);
    }

    public async Task<Guid?> ResolveByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        // Project to id only — order validation runs this against every
        // PickupLocationCode/DropLocationCode in the order, often dozens
        // per submission. Avoid materializing the full aggregate.
        var id = await _dbContext.Warehouses
            .AsNoTracking()
            .Where(w => EF.Functions.ILike(w.Code, code))
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id;
    }

    public async Task<IReadOnlyDictionary<string, Guid>> ResolveBatchAsync(
        IEnumerable<string> codes,
        CancellationToken cancellationToken = default)
    {
        // Normalize input — dedupe + drop blanks. Caller usually passes
        // raw operator/upstream input; defensive trim keeps the query
        // tight and the case-insensitive dictionary stable.
        var distinct = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // Single round trip — fetch every code at once. ToLower on both
        // sides lets Postgres use the standard btree index if we add a
        // functional index later; for now plain ILike works and the
        // table is small (warehouse count <100 in practice).
        var lowered = distinct.Select(c => c.ToLowerInvariant()).ToList();
        var matched = await _dbContext.Warehouses
            .AsNoTracking()
            .Where(w => lowered.Contains(w.Code.ToLower()))
            .Select(w => new { w.Code, w.Id })
            .ToListAsync(cancellationToken);

        // Build the result map keyed by the CALLER's original casing —
        // they passed "WH-bkk-01"; they want "WH-bkk-01" back as the key
        // (not "WH-BKK-01" from the DB).
        var byLower = matched.ToDictionary(m => m.Code.ToLowerInvariant(), m => m.Id);
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in distinct)
        {
            if (byLower.TryGetValue(input.ToLowerInvariant(), out var id))
                result[input] = id;
        }

        return result;
    }

    public async Task<IReadOnlyList<Warehouse>> ListAsync(
        TransportMode? serviceMode = null,
        bool excludeInactive = true,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Warehouses.AsQueryable();

        if (excludeInactive)
            query = query.Where(w => w.IsActive);

        // ServiceModes is jsonb (List<TransportMode> serialized as string array).
        // EF can't translate IReadOnlyCollection.Contains on a value-converted
        // collection, so we filter in memory. Acceptable — warehouse count is
        // bounded; if it grows beyond ~1000 we add a generated column +
        // GIN index on the jsonb path.
        var all = await query
            .OrderBy(w => w.Code)
            .ToListAsync(cancellationToken);

        if (serviceMode is null) return all;

        return all.Where(w => w.ServesMode(serviceMode.Value)).ToList();
    }

    public Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken = default)
    {
        return _dbContext.Warehouses.AddAsync(warehouse, cancellationToken).AsTask();
    }

    public void Update(Warehouse warehouse)
    {
        _dbContext.Warehouses.Update(warehouse);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
