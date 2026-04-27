using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Repositories;

public class RouteEdgeRepository : IRouteEdgeRepository
{
    private readonly FacilityDbContext _db;
    public RouteEdgeRepository(FacilityDbContext db) => _db = db;

    public Task<RouteEdge?> GetBetweenAsync(Guid fromStationId, Guid toStationId, CancellationToken ct = default)
        => _db.RouteEdges.FirstOrDefaultAsync(e =>
            (e.SourceStationId == fromStationId && e.TargetStationId == toStationId) ||
            (e.IsBidirectional && e.SourceStationId == toStationId && e.TargetStationId == fromStationId), ct);

    public Task<List<RouteEdge>> GetByMapAsync(Guid mapId, CancellationToken ct = default)
        => _db.RouteEdges.Where(e => e.MapId == mapId).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
