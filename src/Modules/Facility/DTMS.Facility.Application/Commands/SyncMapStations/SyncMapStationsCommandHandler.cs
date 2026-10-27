using DTMS.Facility.Application.Services;
using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.Repositories;
using DTMS.Facility.Domain.Services;
using DTMS.Facility.Domain.ValueObjects;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace DTMS.Facility.Application.Commands.SyncMapStations;

internal sealed class SyncMapStationsCommandHandler
    : ICommandHandler<SyncMapStationsCommand, SyncMapStationsResult>
{
    private readonly IMapRepository _mapRepo;
    private readonly IStationRepository _stationRepo;
    private readonly IRiot3FacilityClient _riot3;
    private readonly IDistributedCache _cache;
    private readonly StationSyncOptions _options;
    private readonly ILogger<SyncMapStationsCommandHandler> _logger;

    public SyncMapStationsCommandHandler(
        IMapRepository mapRepo,
        IStationRepository stationRepo,
        IRiot3FacilityClient riot3,
        IDistributedCache cache,
        StationSyncOptions options,
        ILogger<SyncMapStationsCommandHandler> logger)
    {
        _mapRepo = mapRepo;
        _stationRepo = stationRepo;
        _riot3 = riot3;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<Result<SyncMapStationsResult>> Handle(
        SyncMapStationsCommand request, CancellationToken ct)
    {
        var map = await _mapRepo.GetByIdAsync(request.MapId, ct);
        if (map is null)
            return Result<SyncMapStationsResult>.Failure($"Map {request.MapId} not found.");

        if (string.IsNullOrWhiteSpace(map.VendorRef))
            return Result<SyncMapStationsResult>.Failure(
                $"Map {map.Name} has no VendorRef — not linked to RIOT3.");

        if (!int.TryParse(map.VendorRef, out var riot3MapId))
            return Result<SyncMapStationsResult>.Failure(
                $"Map {map.Name} has non-integer VendorRef '{map.VendorRef}'.");

        // Shared with the purge — see MapStationLocks. Different maps still run in parallel.
        var semaphore = MapStationLocks.For(map.Id);
        if (!await semaphore.WaitAsync(TimeSpan.Zero, ct))
            return Result<SyncMapStationsResult>.Failure(
                $"Another sync or purge for map {map.Name} is in progress.");

        try
        {
            List<Riot3StationInfo> riot3Stations;
            try
            {
                riot3Stations = await _riot3.GetStationsAsync(riot3MapId, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                // Surface as Failure rather than throwing — otherwise the empty-list contract
                // would have soft-deleted every station on this map.
                return Result<SyncMapStationsResult>.Failure(
                    $"RIOT3 unreachable while syncing map {map.Name}: {ex.Message}");
            }

            // A duplicated id in the payload used to throw out of ToDictionary and
            // fail the whole sync — keep the first and carry on.
            var riot3ByVendorRef = new Dictionary<string, Riot3StationInfo>();
            foreach (var s in riot3Stations)
            {
                if (!riot3ByVendorRef.TryAdd(s.Id.ToString(), s))
                    _logger.LogWarning(
                        "SyncMapStations: map {Name} — RIOT3 listed station id {Id} twice; keeping the first",
                        map.Name, s.Id);
            }

            var allStations = await _stationRepo.GetAllByMapAsync(map.Id, ct);
            // VendorRef-only — manual stations (no VendorRef) are off-limits to RIOT3 sync.
            var dbStations = allStations.Where(s => s.VendorRef != null).ToList();
            var dbByVendorRef = dbStations.ToDictionary(s => s.VendorRef!);

            // Last line of defence against a bad-but-successful answer: a live map
            // never legitimately drops to zero stations, so refuse rather than
            // deactivate everything.
            if (riot3ByVendorRef.Count == 0 && dbStations.Any(s => s.IsActive))
                return Result<SyncMapStationsResult>.Failure(
                    $"RIOT3 returned no stations for map {map.Name} — refusing to deactivate " +
                    $"{dbStations.Count(s => s.IsActive)} active stations.");

            // Rows RIOT3 no longer knows by id. A re-created station (same name,
            // new id) takes one of these over instead of colliding on the
            // (MapId, Code) unique index — which used to fail SaveChanges and
            // stall the whole map's sync on every cycle.
            var removed = dbStations.Where(s => !riot3ByVendorRef.ContainsKey(s.VendorRef!)).ToList();
            var claimed = new HashSet<Guid>();
            var codesInUse = new HashSet<string>(
                allStations.Where(s => s.Code != null).Select(s => s.Code!), StringComparer.Ordinal);
            var touched = new List<Station>();

            int added = 0, updated = 0, deactivated = 0, reactivated = 0, rebound = 0, codeConflicts = 0;

            foreach (var (vendorRef, riot3Station) in riot3ByVendorRef)
            {
                var yaw = riot3Station.PosYaw / 1000.0;

                if (dbByVendorRef.TryGetValue(vendorRef, out var existing))
                {
                    // Raw signed coords — RIOT3's origin can put points in any
                    // quadrant. Mirroring with Math.Abs (older behaviour) broke
                    // station ↔ robot spatial alignment whenever either crossed
                    // the origin.
                    var wasActive = existing.IsActive;
                    if (!existing.UpdateFromVendor(riot3Station.Name, riot3Station.PosX, riot3Station.PosY, yaw))
                        continue;

                    if (!wasActive && existing.IsActive)
                    {
                        reactivated++;
                        touched.Add(existing);
                    }
                    else updated++;
                    continue;
                }

                var takeover = FindTakeover(removed, claimed, riot3Station.Name);
                if (takeover is not null)
                {
                    claimed.Add(takeover.Id);
                    var oldRef = takeover.VendorRef;
                    takeover.SetVendorRef(vendorRef);
                    takeover.UpdateFromVendor(riot3Station.Name, riot3Station.PosX, riot3Station.PosY, yaw);
                    touched.Add(takeover);
                    rebound++;
                    _logger.LogInformation(
                        "SyncMapStations: map {Name} — station {Code} re-created in RIOT3 (id {OldRef} → {NewRef}); kept existing row {StationId}",
                        map.Name, takeover.Code, oldRef, vendorRef, takeover.Id);
                    continue;
                }

                var station = new Station(
                    Guid.NewGuid(), map.Id, riot3Station.Name,
                    new Coordinate(riot3Station.PosX, riot3Station.PosY, yaw), StationType.Normal);
                station.SetVendorRef(vendorRef);

                var code = StationCodeSearch.NormalizeExact(riot3Station.Name);
                if (code.Length > 0 && codesInUse.Add(code))
                {
                    station.SetCode(code);
                }
                else
                {
                    // Added without a Code rather than failing the whole sync on the
                    // unique index; orders can't target it until someone sets one.
                    codeConflicts++;
                    _logger.LogWarning(
                        "SyncMapStations: map {Name} — new RIOT3 station {VendorRef} '{StationName}' added without a Code: '{Code}' is already used on this map",
                        map.Name, vendorRef, riot3Station.Name, code);
                }

                await _stationRepo.AddAsync(station, ct);
                added++;
            }

            var toDeactivate = removed.Where(s => s.IsActive && !claimed.Contains(s.Id)).ToList();
            var activeBefore = dbStations.Count(s => s.IsActive);
            // Max(1, …) so a small map can still lose a single station unattended.
            var allowed = Math.Max(1, activeBefore * _options.MaxDeactivateFraction);
            List<string>? held = null;

            if (toDeactivate.Count > allowed && !request.ConfirmMassDeactivation)
            {
                held = toDeactivate.Select(s => s.Code ?? s.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                _logger.LogWarning(
                    "SyncMapStations: map {Name} — RIOT3 no longer lists {Count} of {Active} active stations (limit {Limit:P0}); holding the deactivation until confirmed: {Stations}",
                    map.Name, toDeactivate.Count, activeBefore, _options.MaxDeactivateFraction, string.Join(", ", held));
            }
            else
            {
                foreach (var dbStation in toDeactivate)
                {
                    dbStation.Deactivate();
                    touched.Add(dbStation);
                    deactivated++;
                }
            }

            await _stationRepo.SaveChangesAsync(ct);
            await InvalidateLookupCacheAsync(touched, ct);

            _logger.LogInformation(
                "SyncMapStations: map {MapId} ({Name}) — added={Added} updated={Updated} reactivated={Reactivated} deactivated={Deactivated} rebound={Rebound} codeConflicts={CodeConflicts} held={Held}",
                map.Id, map.Name, added, updated, reactivated, deactivated, rebound, codeConflicts, held?.Count ?? 0);

            return Result<SyncMapStationsResult>.Success(new SyncMapStationsResult(
                map.Id, map.Name, added, updated, reactivated, deactivated, rebound, codeConflicts, held));
        }
        finally
        {
            semaphore.Release();
        }
    }

    // Exact RIOT3 name first (survives an operator-edited Code such as
    // PREFORM → PREFORM01), then the Code the new station would have taken.
    private static Station? FindTakeover(List<Station> removed, HashSet<Guid> claimed, string riot3Name)
    {
        var code = StationCodeSearch.NormalizeExact(riot3Name);
        return removed.FirstOrDefault(s => !claimed.Contains(s.Id) && s.Name == riot3Name)
            ?? (code.Length == 0
                ? null
                : removed.FirstOrDefault(s => !claimed.Contains(s.Id) && s.Code == code));
    }

    /// <summary>
    /// Drops the submit-time lookup cache for stations whose availability changed,
    /// so order validation sees it now instead of after the TTL. Key format mirrors
    /// CachedStationLookup in the DeliveryOrder module — keep these in sync.
    /// </summary>
    private async Task InvalidateLookupCacheAsync(List<Station> stations, CancellationToken ct)
    {
        foreach (var station in stations)
        {
            try
            {
                await _cache.RemoveAsync($"station:lookup:{station.Id}", ct);
                if (!string.IsNullOrWhiteSpace(station.Code))
                    await _cache.RemoveAsync($"station:lookup:{station.Code}", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SyncMapStations: cache invalidation failed for {StationId}; falling back to TTL expiry.", station.Id);
                return;
            }
        }
    }
}
