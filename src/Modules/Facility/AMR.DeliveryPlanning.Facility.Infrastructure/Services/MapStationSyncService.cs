using AMR.DeliveryPlanning.Facility.Application.Services;
using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.ValueObjects;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Services;

/// <summary>
/// Periodically syncs Map and Station data from RIOT3.
/// - Adds new stations found in RIOT3 but not in DTMS
/// - Updates name/position of existing stations
/// - Soft-deletes (IsActive=false) stations removed from RIOT3
/// - Reactivates stations that reappear in RIOT3
/// Runs before RouteEdgeSyncService (15s delay vs 2min delay).
/// </summary>
public sealed class MapStationSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRiot3FacilityClient _riot3;
    private readonly ILogger<MapStationSyncService> _logger;
    private readonly TimeSpan _interval;

    public MapStationSyncService(
        IServiceScopeFactory scopeFactory,
        IRiot3FacilityClient riot3,
        ILogger<MapStationSyncService> logger,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _riot3 = riot3;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncAllMapsAsync(stoppingToken);
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task SyncAllMapsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();

        var maps = await db.Maps
            .Where(m => m.VendorRef != null)
            .ToListAsync(ct);

        if (maps.Count == 0)
        {
            _logger.LogDebug("MapStationSyncService: no maps with VendorRef — skipping sync");
            return;
        }

        _logger.LogInformation("MapStationSyncService: syncing stations for {Count} maps", maps.Count);

        foreach (var map in maps)
        {
            if (ct.IsCancellationRequested) break;
            await SyncMapAsync(db, map, ct);
        }
    }

    private async Task SyncMapAsync(FacilityDbContext db, Map map, CancellationToken ct)
    {
        if (!int.TryParse(map.VendorRef, out var riot3MapId))
        {
            _logger.LogWarning("MapStationSyncService: map {MapId} has non-integer VendorRef '{VendorRef}' — skipping",
                map.Id, map.VendorRef);
            return;
        }

        // Fetch stations from RIOT3
        var riot3Stations = await _riot3.GetStationsAsync(riot3MapId, ct);
        var riot3ById = riot3Stations.ToDictionary(s => s.Id.ToString());

        // Fetch all stations from DB (including inactive)
        var dbStations = await db.Stations
            .Where(s => s.MapId == map.Id && s.VendorRef != null)
            .ToListAsync(ct);
        var dbByVendorRef = dbStations.ToDictionary(s => s.VendorRef!);

        int added = 0, updated = 0, deactivated = 0, reactivated = 0;

        // Add new / update existing / reactivate
        foreach (var (vendorRef, riot3Station) in riot3ById)
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
                // New station in RIOT3
                var coord = new Coordinate(
                    Math.Abs(riot3Station.PosX),
                    Math.Abs(riot3Station.PosY),
                    riot3Station.PosYaw / 1000.0);

                var station = new Station(Guid.NewGuid(), map.Id, riot3Station.Name, coord, StationType.Normal);
                station.SetVendorRef(riot3Station.Id.ToString());
                station.SetCode(riot3Station.Name);
                await db.Stations.AddAsync(station, ct);
                added++;
            }
        }

        // Soft-delete stations no longer in RIOT3
        foreach (var dbStation in dbStations.Where(s => !riot3ById.ContainsKey(s.VendorRef!)))
        {
            if (dbStation.IsActive)
            {
                dbStation.Deactivate();
                deactivated++;
            }
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "MapStationSyncService: map {MapId} ({Name}) — added={Added} updated={Updated} reactivated={Reactivated} deactivated={Deactivated}",
            map.Id, map.Name, added, updated, reactivated, deactivated);
    }
}
