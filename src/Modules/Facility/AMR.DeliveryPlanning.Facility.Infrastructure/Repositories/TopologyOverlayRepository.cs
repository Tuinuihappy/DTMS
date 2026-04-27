using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Repositories;

public class TopologyOverlayRepository : ITopologyOverlayRepository
{
    private readonly FacilityDbContext _db;
    public TopologyOverlayRepository(FacilityDbContext db) => _db = db;

    public Task<TopologyOverlay?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.TopologyOverlays.FindAsync(new object[] { id }, ct).AsTask();

    public Task<List<TopologyOverlay>> GetActiveByMapAsync(Guid mapId, CancellationToken ct = default)
        => _db.TopologyOverlays
            .Where(o => o.MapId == mapId && o.ValidFrom <= DateTime.UtcNow && o.ValidUntil > DateTime.UtcNow)
            .ToListAsync(ct);

    public Task<List<TopologyOverlay>> GetExpiredAsync(CancellationToken ct = default)
        => _db.TopologyOverlays.Where(o => o.ValidUntil <= DateTime.UtcNow).ToListAsync(ct);

    public async Task AddAsync(TopologyOverlay overlay, CancellationToken ct = default)
        => await _db.TopologyOverlays.AddAsync(overlay, ct);

    public Task UpdateAsync(TopologyOverlay overlay, CancellationToken ct = default)
    {
        _db.TopologyOverlays.Update(overlay);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
