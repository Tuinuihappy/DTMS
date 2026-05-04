using AMR.DeliveryPlanning.Facility.Application.Services;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Services;

public sealed class FacilityReadService : IFacilityReadService
{
    private readonly FacilityDbContext _db;

    public FacilityReadService(FacilityDbContext db)
    {
        _db = db;
    }

    public Task<bool> StationExistsAsync(Guid stationId, CancellationToken cancellationToken = default)
        => _db.Stations.AnyAsync(s => s.Id == stationId, cancellationToken);

    public Task<Guid?> ResolveStationByCodeAsync(string code, CancellationToken cancellationToken = default)
        => _db.Stations.AsNoTracking()
            .Where(s => s.Code == code)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<StationVendorTarget?> GetStationVendorTargetAsync(
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        var target = await (
            from station in _db.Stations.AsNoTracking()
            join map in _db.Maps.AsNoTracking() on station.MapId equals map.Id
            where station.Id == stationId
            select new
            {
                StationId = station.Id,
                station.MapId,
                MapVendorRef = map.VendorRef,
                StationVendorRef = station.VendorRef
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (target == null ||
            string.IsNullOrWhiteSpace(target.MapVendorRef) ||
            string.IsNullOrWhiteSpace(target.StationVendorRef))
        {
            return null;
        }

        return new StationVendorTarget(
            target.StationId,
            target.MapId,
            target.MapVendorRef.Trim(),
            target.StationVendorRef.Trim());
    }

    public async Task<double?> GetRouteCostAsync(
        Guid fromStationId,
        Guid toStationId,
        CancellationToken cancellationToken = default)
    {
        var edge = await _db.RouteEdges.AsNoTracking().FirstOrDefaultAsync(e =>
            (e.SourceStationId == fromStationId && e.TargetStationId == toStationId) ||
            (e.IsBidirectional && e.SourceStationId == toStationId && e.TargetStationId == fromStationId),
            cancellationToken);

        return edge?.Cost;
    }
}
