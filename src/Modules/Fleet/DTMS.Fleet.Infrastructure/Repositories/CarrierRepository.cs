using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Enums;
using DTMS.Fleet.Domain.Repositories;
using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Fleet.Infrastructure.Repositories;

public class CarrierRepository : ICarrierRepository
{
    private readonly FleetDbContext _db;

    public CarrierRepository(FleetDbContext db) => _db = db;

    public Task<Carrier?> GetByCodeAsync(string carrierCode, CancellationToken ct = default)
        => _db.Carriers.FirstOrDefaultAsync(c => c.CarrierCode == carrierCode.Trim().ToUpperInvariant(), ct);

    public Task<Carrier?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Carriers.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> CodeExistsAsync(string carrierCode, CancellationToken ct = default)
        => _db.Carriers.AnyAsync(c => c.CarrierCode == carrierCode.Trim().ToUpperInvariant(), ct);

    public Task<bool> BarcodeExistsAsync(string barcode, Guid? excludeId = null, CancellationToken ct = default)
    {
        var trimmed = barcode.Trim();
        var q = _db.Carriers.Where(c => c.Barcode == trimmed);
        if (excludeId.HasValue) q = q.Where(c => c.Id != excludeId.Value);
        return q.AnyAsync(ct);
    }

    public async Task<(IReadOnlyList<Carrier> Rows, int TotalCount)> SearchAsync(
        CarrierStatus? status,
        Guid? carrierTypeId,
        string? query,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        // Plain LINQ, never FromSqlInterpolated: its p0..pN placeholders collide
        // with the @p0 EF generates for LIMIT, which fails only from page 2 with
        // a 42804 — a bug this codebase has already paid for once.
        var filtered = _db.Carriers.AsNoTracking().AsQueryable();

        if (status.HasValue)
            filtered = filtered.Where(c => c.Status == status.Value);
        if (carrierTypeId.HasValue)
            filtered = filtered.Where(c => c.CarrierTypeId == carrierTypeId.Value);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            filtered = filtered.Where(c =>
                EF.Functions.ILike(c.CarrierCode, pattern) ||
                (c.DisplayName != null && EF.Functions.ILike(c.DisplayName, pattern)) ||
                (c.Barcode != null && EF.Functions.ILike(c.Barcode, pattern)));
        }

        var totalCount = await filtered.CountAsync(ct);

        var rows = await filtered
            .OrderBy(c => c.CarrierCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (rows, totalCount);
    }

    public async Task AddAsync(Carrier carrier, CancellationToken ct = default)
        => await _db.Carriers.AddAsync(carrier, ct);

    public void Remove(Carrier carrier) => _db.Carriers.Remove(carrier);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
