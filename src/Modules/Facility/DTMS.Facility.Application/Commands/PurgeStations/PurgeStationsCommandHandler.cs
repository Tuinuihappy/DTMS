using DTMS.Facility.Application.Services;
using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.Repositories;
using DTMS.Facility.Domain.Services;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace DTMS.Facility.Application.Commands.PurgeStations;

internal sealed class PurgeStationsCommandHandler
    : ICommandHandler<PurgeStationsCommand, PurgeStationsResult>
{
    private readonly IMapRepository _mapRepo;
    private readonly IStationRepository _stationRepo;
    private readonly IRiot3FacilityClient _riot3;
    private readonly IStationUsageReader _usage;
    private readonly IDistributedCache _cache;
    private readonly ILogger<PurgeStationsCommandHandler> _logger;

    public PurgeStationsCommandHandler(
        IMapRepository mapRepo,
        IStationRepository stationRepo,
        IRiot3FacilityClient riot3,
        IStationUsageReader usage,
        IDistributedCache cache,
        ILogger<PurgeStationsCommandHandler> logger)
    {
        _mapRepo = mapRepo;
        _stationRepo = stationRepo;
        _riot3 = riot3;
        _usage = usage;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<PurgeStationsResult>> Handle(PurgeStationsCommand request, CancellationToken ct)
    {
        var map = await _mapRepo.GetByIdAsync(request.MapId, ct);
        if (map is null)
            return Result<PurgeStationsResult>.Failure($"Map {request.MapId} not found.");
        if (!int.TryParse(map.VendorRef, out var riot3MapId))
            return Result<PurgeStationsResult>.Failure($"Map {map.Name} is not linked to RIOT3.");

        var semaphore = MapStationLocks.For(map.Id);
        if (!await semaphore.WaitAsync(TimeSpan.Zero, ct))
            return Result<PurgeStationsResult>.Failure(
                $"Another sync or purge for map {map.Name} is in progress — try again in a moment.");

        try
        {
            // Ask RIOT3 now rather than trusting the last sync: up to 30 min old.
            List<Riot3StationInfo> live;
            try
            {
                live = await _riot3.GetStationsAsync(riot3MapId, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                return Result<PurgeStationsResult>.Failure(
                    $"Couldn't confirm with RIOT3 that the stations are gone, nothing was deleted: {ex.Message}");
            }
            if (live.Count == 0)
                return Result<PurgeStationsResult>.Failure(
                    $"RIOT3 returned no stations for map {map.Name} — nothing was deleted.");

            var liveIds = live.Select(s => s.Id.ToString()).ToHashSet(StringComparer.Ordinal);
            var liveNames = live.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            var liveCodes = live.Select(s => StationCodeSearch.NormalizeExact(s.Name)).ToHashSet(StringComparer.Ordinal);

            var all = await _stationRepo.GetAllByMapAsync(map.Id, ct);
            var skipped = new List<SkippedStation>();
            List<Station> candidates;
            if (request.StationIds is null)
            {
                candidates = all.Where(s => !s.IsActive && s.VendorRef != null).ToList();
            }
            else
            {
                var byId = all.ToDictionary(s => s.Id);
                candidates = [];
                foreach (var id in request.StationIds.Distinct())
                {
                    if (!byId.TryGetValue(id, out var s))
                        skipped.Add(new SkippedStation(id, id.ToString(), "not a station on this map"));
                    else if (s.VendorRef is null)
                        skipped.Add(new SkippedStation(id, Label(s), "added by hand, not from RIOT3"));
                    else if (s.IsActive)
                        skipped.Add(new SkippedStation(id, Label(s), "still active"));
                    else
                        candidates.Add(s);
                }
            }

            var stillInRiot = new List<Station>();
            foreach (var s in candidates)
            {
                if (liveIds.Contains(s.VendorRef!))
                    skipped.Add(new SkippedStation(s.Id, Label(s), "RIOT3 still has it — the next sync reactivates it"));
                // Same rule the sync uses to take a removed row over — deleting it
                // would throw away the Type/Code that takeover is meant to keep.
                else if (liveNames.Contains(s.Name) || (s.Code is not null && liveCodes.Contains(s.Code)))
                    skipped.Add(new SkippedStation(s.Id, Label(s), "RIOT3 has a station with the same name — the next sync takes this row over"));
                else
                    continue;
                stillInRiot.Add(s);
            }
            candidates = candidates.Except(stillInRiot).ToList();

            var inUse = candidates.Count == 0
                ? new Dictionary<Guid, string>()
                : await _usage.FindInUseAsync(candidates.Select(s => s.Id).ToList(), ct);

            var purged = new List<PurgedStation>();
            var removed = new List<Station>();
            foreach (var s in candidates)
            {
                if (inUse.TryGetValue(s.Id, out var reason))
                {
                    skipped.Add(new SkippedStation(s.Id, Label(s), reason));
                    continue;
                }
                _stationRepo.Remove(s);
                removed.Add(s);
                purged.Add(new PurgedStation(s.Id, Label(s)));
            }

            if (removed.Count > 0)
            {
                await _stationRepo.SaveChangesAsync(ct);
                await InvalidateLookupCacheAsync(removed, ct);
            }

            _logger.LogInformation(
                "PurgeStations: map {Name} — purged {Purged} ({PurgedList}), skipped {Skipped}",
                map.Name, purged.Count, string.Join(", ", purged.Select(p => p.Label)), skipped.Count);

            return Result<PurgeStationsResult>.Success(new PurgeStationsResult(purged, skipped));
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static string Label(Station s) => s.Code ?? s.Name;

    // Same keys as SyncMapStationsCommandHandler / CachedStationLookup.
    private async Task InvalidateLookupCacheAsync(List<Station> stations, CancellationToken ct)
    {
        try
        {
            foreach (var s in stations)
            {
                await _cache.RemoveAsync($"station:lookup:{s.Id}", ct);
                if (!string.IsNullOrWhiteSpace(s.Code))
                    await _cache.RemoveAsync($"station:lookup:{s.Code}", ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PurgeStations: cache invalidation failed; falling back to TTL expiry.");
        }
    }
}
