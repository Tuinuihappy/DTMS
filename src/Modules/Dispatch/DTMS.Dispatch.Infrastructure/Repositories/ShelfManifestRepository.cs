using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Repositories;

public class ShelfManifestRepository : IShelfManifestRepository
{
    private readonly DispatchDbContext _db;

    public ShelfManifestRepository(DispatchDbContext db) => _db = db;

    public Task<ShelfManifest?> GetByJobIdAsync(Guid jobId, CancellationToken ct = default)
        => _db.ShelfManifests.FirstOrDefaultAsync(s => s.JobId == jobId, ct);

    public Task<ShelfManifest?> GetByTripIdAsync(Guid tripId, CancellationToken ct = default)
        => _db.ShelfManifests.FirstOrDefaultAsync(s => s.TripId == tripId, ct);

    public Task<List<ShelfManifest>> GetByShelfRfidAsync(string shelfRfid, CancellationToken ct = default)
        => _db.ShelfManifests.Where(s => s.ShelfRfid == shelfRfid).ToListAsync(ct);

    public async Task AddAsync(ShelfManifest manifest, CancellationToken ct = default)
        => await _db.ShelfManifests.AddAsync(manifest, ct);

    public Task UpdateAsync(ShelfManifest manifest, CancellationToken ct = default)
    {
        _db.ShelfManifests.Update(manifest);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
