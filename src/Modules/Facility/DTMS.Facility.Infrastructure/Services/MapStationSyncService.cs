using DTMS.Facility.Application.Commands.SyncMapStations;
using DTMS.Facility.Infrastructure.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DTMS.Facility.Infrastructure.Services;

/// <summary>
/// Periodically dispatches <see cref="SyncMapStationsCommand"/> for every map with a
/// RIOT3 VendorRef. The actual add/update/deactivate logic lives in the command handler
/// so the manual sync endpoint and this background poller share one code path.
/// Runs before RouteEdgeSyncService (15s delay vs 2min delay).
/// </summary>
public sealed class MapStationSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MapStationSyncService> _logger;
    private readonly TimeSpan _interval;

    public MapStationSyncService(
        IServiceScopeFactory scopeFactory,
        ILogger<MapStationSyncService> logger,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAllMapsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MapStationSyncService: sync cycle failed — will retry next interval");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task SyncAllMapsAsync(CancellationToken ct)
    {
        List<Guid> mapIds;
        using (var listScope = _scopeFactory.CreateScope())
        {
            var db = listScope.ServiceProvider.GetRequiredService<FacilityDbContext>();
            mapIds = await db.Maps
                .Where(m => m.VendorRef != null)
                .Select(m => m.Id)
                .ToListAsync(ct);
        }

        if (mapIds.Count == 0)
        {
            _logger.LogDebug("MapStationSyncService: no maps with VendorRef — skipping sync");
            return;
        }

        _logger.LogInformation("MapStationSyncService: syncing stations for {Count} maps", mapIds.Count);

        foreach (var mapId in mapIds)
        {
            if (ct.IsCancellationRequested) break;

            // Fresh scope per map so the DbContext isn't reused across iterations.
            using var scope = _scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            try
            {
                var result = await sender.Send(new SyncMapStationsCommand(mapId), ct);
                if (result.IsFailure)
                {
                    _logger.LogWarning(
                        "MapStationSyncService: map {MapId} sync failed — {Error}",
                        mapId, result.Error);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "MapStationSyncService: map {MapId} sync threw — continuing with next map",
                    mapId);
            }
        }
    }
}
