using DTMS.Facility.Application.Services;
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

    public Task<bool> StationExistsAsync(Guid stationId, CancellationToken cancellationToken = default)
        => _db.Stations.AnyAsync(s => s.Id == stationId, cancellationToken);

    public Task<Guid?> ResolveStationByCodeAsync(string code, CancellationToken cancellationToken = default)
        => _db.Stations.AsNoTracking()
            .Where(s => s.Code == code)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Guid?> ResolveStationByVendorRefAsync(string vendorRef, CancellationToken cancellationToken = default)
        => _db.Stations.AsNoTracking()
            .Where(s => s.VendorRef == vendorRef)
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
                StationVendorRef = station.VendorRef,
                station.Actions
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (target == null ||
            string.IsNullOrWhiteSpace(target.MapVendorRef) ||
            string.IsNullOrWhiteSpace(target.StationVendorRef))
        {
            return null;
        }

        // Project the domain value-object map into the plain Application DTO
        // so callers (Dispatch) don't import Facility.Domain.
        IReadOnlyDictionary<string, StationActionConfig>? actions = null;
        if (target.Actions is { Count: > 0 })
        {
            var projected = new Dictionary<string, StationActionConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in target.Actions)
            {
                projected[kv.Key] = new StationActionConfig(
                    kv.Value.ActionType,
                    kv.Value.Category,
                    kv.Value.Parameters);
            }
            actions = projected;
        }

        return new StationVendorTarget(
            target.StationId,
            target.MapId,
            target.MapVendorRef.Trim(),
            target.StationVendorRef.Trim(),
            actions);
    }

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
                .Where(s => s.Code != null && upperCodes.Contains(s.Code))
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
