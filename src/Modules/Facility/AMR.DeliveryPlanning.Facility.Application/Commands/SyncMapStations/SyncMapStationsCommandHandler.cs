using System.Collections.Concurrent;
using AMR.DeliveryPlanning.Facility.Application.Services;
using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.SyncMapStations;

internal sealed class SyncMapStationsCommandHandler
    : ICommandHandler<SyncMapStationsCommand, SyncMapStationsResult>
{
    // Per-map lock — shared between the background poller and any manual trigger so two
    // concurrent syncs on the same map can't race on the Stations table (unique-index
    // violation, double-add, etc.). Different maps still run in parallel.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    private readonly IMapRepository _mapRepo;
    private readonly IStationRepository _stationRepo;
    private readonly IRiot3FacilityClient _riot3;
    private readonly ILogger<SyncMapStationsCommandHandler> _logger;

    public SyncMapStationsCommandHandler(
        IMapRepository mapRepo,
        IStationRepository stationRepo,
        IRiot3FacilityClient riot3,
        ILogger<SyncMapStationsCommandHandler> logger)
    {
        _mapRepo = mapRepo;
        _stationRepo = stationRepo;
        _riot3 = riot3;
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

        var semaphore = _locks.GetOrAdd(map.Id, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(TimeSpan.Zero, ct))
            return Result<SyncMapStationsResult>.Failure(
                $"Sync for map {map.Name} is already in progress.");

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

            var riot3ByVendorRef = riot3Stations.ToDictionary(s => s.Id.ToString());

            // VendorRef-only — manual stations (no VendorRef) are off-limits to RIOT3 sync.
            var dbStations = (await _stationRepo.GetAllByMapAsync(map.Id, ct))
                .Where(s => s.VendorRef != null)
                .ToList();
            var dbByVendorRef = dbStations.ToDictionary(s => s.VendorRef!);

            int added = 0, updated = 0, deactivated = 0, reactivated = 0;

            foreach (var (vendorRef, riot3Station) in riot3ByVendorRef)
            {
                if (dbByVendorRef.TryGetValue(vendorRef, out var existing))
                {
                    existing.UpdateFromVendor(
                        riot3Station.Name,
                        Math.Abs(riot3Station.PosX),
                        Math.Abs(riot3Station.PosY),
                        riot3Station.PosYaw / 1000.0);

                    if (!existing.IsActive) reactivated++;
                    else updated++;
                }
                else
                {
                    var coord = new Coordinate(
                        Math.Abs(riot3Station.PosX),
                        Math.Abs(riot3Station.PosY),
                        riot3Station.PosYaw / 1000.0);

                    var station = new Station(Guid.NewGuid(), map.Id, riot3Station.Name, coord, StationType.Normal);
                    station.SetVendorRef(riot3Station.Id.ToString());
                    station.SetCode(riot3Station.Name);
                    await _stationRepo.AddAsync(station, ct);
                    added++;
                }
            }

            foreach (var dbStation in dbStations.Where(s => !riot3ByVendorRef.ContainsKey(s.VendorRef!)))
            {
                if (dbStation.IsActive)
                {
                    dbStation.Deactivate();
                    deactivated++;
                }
            }

            await _stationRepo.SaveChangesAsync(ct);

            _logger.LogInformation(
                "SyncMapStations: map {MapId} ({Name}) — added={Added} updated={Updated} reactivated={Reactivated} deactivated={Deactivated}",
                map.Id, map.Name, added, updated, reactivated, deactivated);

            return Result<SyncMapStationsResult>.Success(new SyncMapStationsResult(
                map.Id, map.Name, added, updated, reactivated, deactivated));
        }
        finally
        {
            semaphore.Release();
        }
    }
}
