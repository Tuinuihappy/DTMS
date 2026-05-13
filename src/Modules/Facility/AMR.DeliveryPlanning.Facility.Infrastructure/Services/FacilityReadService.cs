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

    public async Task<IReadOnlyDictionary<string, Guid>> ResolveStationsBatchAsync(
        IReadOnlyList<string> locationCodes,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var guidInputs = locationCodes.Where(c => Guid.TryParse(c, out _)).ToList();
        var codeInputs = locationCodes.Where(c => !Guid.TryParse(c, out _)).ToList();

        if (guidInputs.Count > 0)
        {
            var guids = guidInputs.ConvertAll(Guid.Parse);
            var foundIds = await _db.Stations.AsNoTracking()
                .Where(s => guids.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in foundIds)
                result[id.ToString()] = id;
        }

        if (codeInputs.Count > 0)
        {
            var upperCodes = codeInputs.ConvertAll(c => c.ToUpperInvariant());
            var stations = await _db.Stations.AsNoTracking()
                .Where(s => s.Code != null && upperCodes.Contains(s.Code))
                .Select(s => new { s.Code, s.Id })
                .ToListAsync(cancellationToken);
            foreach (var s in stations)
                result[s.Code!] = s.Id;
        }

        return result;
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
