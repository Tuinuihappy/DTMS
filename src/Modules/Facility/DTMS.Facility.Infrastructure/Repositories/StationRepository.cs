using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.Repositories;
using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Facility.Infrastructure.Repositories;

public class StationRepository : IStationRepository
{
    private readonly FacilityDbContext _db;
    public StationRepository(FacilityDbContext db) => _db = db;

    public Task<Station?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Stations.FindAsync(new object[] { id }, ct).AsTask();

    public Task<List<Station>> GetByMapAsync(Guid mapId, CancellationToken ct = default)
        => _db.Stations.Where(s => s.MapId == mapId && s.IsActive).ToListAsync(ct);

    public Task<List<Station>> GetAllByMapAsync(Guid mapId, CancellationToken ct = default)
        => _db.Stations.Where(s => s.MapId == mapId).ToListAsync(ct);

    public async Task<List<Station>> QueryAsync(Guid? mapId, StationType? type, Guid? zoneId,
        string? compatibleVehicleType, bool includeInactive = false, string? code = null, CancellationToken ct = default)
    {
        var query = includeInactive
            ? _db.Stations.AsQueryable()
            : _db.Stations.Where(s => s.IsActive).AsQueryable();
        if (mapId.HasValue) query = query.Where(s => s.MapId == mapId);
        if (type.HasValue) query = query.Where(s => s.Type == type);
        if (zoneId.HasValue) query = query.Where(s => s.ZoneId == zoneId);
        if (compatibleVehicleType != null)
            query = query.Where(s => EF.Property<string>(s, "CompatibleVehicleTypes").Contains(compatibleVehicleType));
        if (code != null)
            query = query.Where(s => s.Code != null && s.Code.Contains(code));
        return await query.ToListAsync(ct);
    }

    public async Task AddAsync(Station station, CancellationToken ct = default)
        => await _db.Stations.AddAsync(station, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
