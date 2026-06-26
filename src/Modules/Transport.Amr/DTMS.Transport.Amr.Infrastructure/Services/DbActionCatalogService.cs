using AMR.DeliveryPlanning.Transport.Abstractions.Models;
using AMR.DeliveryPlanning.Transport.Abstractions.Services;
using AMR.DeliveryPlanning.Transport.Amr.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Transport.Amr.Infrastructure.Services;

public class DbActionCatalogService : IActionCatalogService
{
    private readonly VendorAdapterDbContext _db;

    public DbActionCatalogService(VendorAdapterDbContext db) => _db = db;

    public async Task<ActionCatalogEntry?> GetAsync(string vehicleTypeKey, string canonicalAction, CancellationToken ct = default)
        => await _db.ActionCatalogEntries
            .FirstOrDefaultAsync(e => e.VehicleTypeKey == vehicleTypeKey && e.CanonicalAction == canonicalAction, ct);

    public async Task<List<ActionCatalogEntry>> GetAllAsync(CancellationToken ct = default)
        => await _db.ActionCatalogEntries.ToListAsync(ct);

    public async Task UpsertAsync(ActionCatalogEntry entry, CancellationToken ct = default)
    {
        var existing = await _db.ActionCatalogEntries.FirstOrDefaultAsync(
            e => e.VehicleTypeKey == entry.VehicleTypeKey && e.CanonicalAction == entry.CanonicalAction, ct);

        if (existing == null)
            await _db.ActionCatalogEntries.AddAsync(entry, ct);
        else
        {
            _db.ActionCatalogEntries.Remove(existing);
            await _db.ActionCatalogEntries.AddAsync(entry, ct);
        }

        await _db.SaveChangesAsync(ct);
    }
}
