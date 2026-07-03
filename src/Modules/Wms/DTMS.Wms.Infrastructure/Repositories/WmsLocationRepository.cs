using DTMS.Wms.Domain.Entities;
using DTMS.Wms.Domain.Repositories;
using DTMS.Wms.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Wms.Infrastructure.Repositories;

/// <summary>
/// EF impl for <see cref="IWmsLocationRepository"/>. Uses
/// AsNoTracking on hot reads and case-insensitive batch lookup on the
/// order-validation hot path.
/// </summary>
public class WmsLocationRepository : IWmsLocationRepository
{
    private readonly WmsDbContext _db;

    public WmsLocationRepository(WmsDbContext db)
    {
        _db = db;
    }

    // ── Read (hot path) ─────────────────────────────────────────────

    public Task<WmsLocation?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) return Task.FromResult<WmsLocation?>(null);
        return _db.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public Task<WmsLocation?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult<WmsLocation?>(null);
        return _db.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => EF.Functions.ILike(l.LocationCode, code), ct);
    }

    public async Task<Guid?> ResolveByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        // Project to id only — order Submit resolves 2–20 codes per request
        // so materializing the aggregate is wasteful.
        var id = await _db.Locations
            .AsNoTracking()
            .Where(l => EF.Functions.ILike(l.LocationCode, code))
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(ct);
        return id;
    }

    public async Task<IReadOnlyDictionary<string, WmsLocation>> ResolveBatchAsync(
        IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        // Normalize input — dedupe, trim, drop blanks. Case-insensitive dictionary
        // stays stable across whatever casing the caller passed.
        var distinct = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
            return new Dictionary<string, WmsLocation>(StringComparer.OrdinalIgnoreCase);

        // Single round trip — fetch every code at once with a lowered IN clause
        // so Postgres can use a functional lower() index if we add one later.
        var lowered = distinct.Select(c => c.ToLowerInvariant()).ToList();
        var matched = await _db.Locations
            .AsNoTracking()
            .Where(l => lowered.Contains(l.LocationCode.ToLower()))
            .ToListAsync(ct);

        var byLower = matched.ToDictionary(m => m.LocationCode.ToLowerInvariant(), m => m);
        var result = new Dictionary<string, WmsLocation>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in distinct)
        {
            if (byLower.TryGetValue(input.ToLowerInvariant(), out var loc))
                result[input] = loc;
        }
        return result;
    }

    public async Task<(IReadOnlyList<WmsLocation> Items, int Total)> QueryAsync(
        string? search,
        string? parentCode,
        int page,
        int pageSize,
        bool includeInactive,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 500) pageSize = 500;

        var q = _db.Locations.AsNoTracking().AsQueryable();

        if (!includeInactive)
            q = q.Where(l => l.IsActive);

        if (!string.IsNullOrWhiteSpace(parentCode))
            q = q.Where(l => EF.Functions.ILike(l.ParentLocationCode!, parentCode));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            q = q.Where(l =>
                EF.Functions.ILike(l.LocationCode, pattern) ||
                EF.Functions.ILike(l.DisplayName, pattern));
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(l => l.LocationCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    // ── Sync (cold path) ────────────────────────────────────────────

    public async Task<IReadOnlyDictionary<string, WmsLocation>> GetAllAsync(CancellationToken ct = default)
    {
        // Sync loads every location (tracked) so the caller can mutate
        // in place; the resulting dict is keyed by lowercase LocationCode
        // for fast diff-by-code.
        var all = await _db.Locations.ToListAsync(ct);
        return all.ToDictionary(
            l => l.LocationCode.ToLowerInvariant(),
            l => l,
            StringComparer.OrdinalIgnoreCase);
    }

    public Task AddAsync(WmsLocation location, CancellationToken ct = default)
        => _db.Locations.AddAsync(location, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
