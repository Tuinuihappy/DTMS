using DTMS.Facility.Application.Services;
using DTMS.Facility.Domain.Services;
using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Facility.Infrastructure.Services;

public sealed class FacilityReadService : IFacilityReadService
{
    private readonly FacilityDbContext _db;

    public FacilityReadService(FacilityDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<UnavailableVendorStation>> FindUnavailableVendorStationsAsync(
        IReadOnlyCollection<VendorStationRef> refs,
        CancellationToken cancellationToken = default)
    {
        if (refs.Count == 0) return [];

        var mapRefs = refs.Select(r => r.MapVendorRef).Distinct().ToList();
        var maps = await _db.Maps.AsNoTracking()
            .Where(m => m.VendorRef != null && mapRefs.Contains(m.VendorRef))
            .Select(m => new { m.Id, m.VendorRef })
            .ToListAsync(cancellationToken);
        if (maps.Count == 0) return [];

        var mapIds = maps.Select(m => m.Id).ToList();
        var stationRefs = refs.Select(r => r.StationVendorRef).Distinct().ToList();
        var stations = await _db.Stations.AsNoTracking()
            .Where(s => mapIds.Contains(s.MapId) && s.VendorRef != null && stationRefs.Contains(s.VendorRef))
            .Select(s => new { s.MapId, s.VendorRef, s.Name, s.IsActive })
            .ToListAsync(cancellationToken);

        var mapIdByRef = maps.ToDictionary(m => m.VendorRef!, m => m.Id);
        var unavailable = new List<UnavailableVendorStation>();
        foreach (var r in refs.Distinct())
        {
            if (!mapIdByRef.TryGetValue(r.MapVendorRef, out var mapId)) continue;

            var station = stations.FirstOrDefault(s => s.MapId == mapId && s.VendorRef == r.StationVendorRef);
            if (station is null)
                unavailable.Add(new UnavailableVendorStation(r.MapVendorRef, r.StationVendorRef, null));
            else if (!station.IsActive)
                unavailable.Add(new UnavailableVendorStation(r.MapVendorRef, r.StationVendorRef, station.Name));
        }
        return unavailable;
    }

    public Task<bool> StationExistsAsync(Guid stationId, CancellationToken cancellationToken = default)
        => _db.Stations.AnyAsync(s => s.Id == stationId, cancellationToken);

    public Task<Guid?> ResolveStationByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        // Stored codes are always TRIM+UPPER (Station.SetCode); normalize the
        // input the same way so lowercase callers still resolve. Stays an
        // exact single-code match — never a substring/fuzzy lookup. The Maps
        // join keeps orphaned stations of a deleted map (whose codes can
        // duplicate the live map's) from being resolved nondeterministically.
        var exact = StationCodeSearch.NormalizeExact(code);
        return _db.Stations.AsNoTracking()
            .Where(s => s.Code == exact && _db.Maps.Any(m => m.Id == s.MapId))
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Guid?> ResolveStationByVendorRefAsync(string vendorRef, CancellationToken cancellationToken = default)
        // Same Maps join as ResolveStationByCodeAsync: deleting a map leaves
        // its Stations rows behind, and a re-imported map reuses RIOT3's
        // numeric station ids — so a VendorRef can match both a live row and
        // an orphan (13 such pairs found 2026-08-05, e.g. 177 = SHELF_FG_1 +
        // orphaned SHELF10). Without the join FirstOrDefault picks
        // nondeterministically; resolving the orphan would silently break
        // pickup/drop detection (its Guid matches neither trip station).
        => _db.Stations.AsNoTracking()
            .Where(s => s.VendorRef == vendorRef && _db.Maps.Any(m => m.Id == s.MapId))
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, StationLookupResult>> ResolveStationsBatchAsync(
        IReadOnlyList<string> locationCodes,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, StationLookupResult>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        var guidInputs = locationCodes.Where(c => Guid.TryParse(c, out _)).ToList();
        var codeInputs = locationCodes.Where(c => !Guid.TryParse(c, out _)).ToList();

        if (guidInputs.Count > 0)
        {
            var guids = guidInputs.ConvertAll(Guid.Parse);
            var found = await _db.Stations.AsNoTracking()
                .Where(s => guids.Contains(s.Id))
                .Select(s => new
                {
                    s.Id,
                    s.Code,
                    s.IsActive,
                    s.ManualOverrideOffline,
                    s.ManualOverrideExpiresAt,
                    s.ManualOverrideReason
                })
                .ToListAsync(cancellationToken);
            foreach (var s in found)
            {
                var overrideActive = s.ManualOverrideOffline
                    && (!s.ManualOverrideExpiresAt.HasValue || now < s.ManualOverrideExpiresAt.Value);
                result[s.Id.ToString()] = new StationLookupResult(
                    s.Id, s.Code, s.IsActive, overrideActive, overrideActive ? s.ManualOverrideReason : null);
            }
        }

        if (codeInputs.Count > 0)
        {
            var upperCodes = codeInputs.ConvertAll(c => c.ToUpperInvariant());
            var stations = await _db.Stations.AsNoTracking()
                .Where(s => s.Code != null && upperCodes.Contains(s.Code)
                    && _db.Maps.Any(m => m.Id == s.MapId))
                .Select(s => new
                {
                    s.Code,
                    s.Id,
                    s.IsActive,
                    s.ManualOverrideOffline,
                    s.ManualOverrideExpiresAt,
                    s.ManualOverrideReason
                })
                .ToListAsync(cancellationToken);
            foreach (var s in stations)
            {
                var overrideActive = s.ManualOverrideOffline
                    && (!s.ManualOverrideExpiresAt.HasValue || now < s.ManualOverrideExpiresAt.Value);
                result[s.Code!] = new StationLookupResult(
                    s.Id, s.Code, s.IsActive, overrideActive, overrideActive ? s.ManualOverrideReason : null);
            }
        }

        return result;
    }
}
