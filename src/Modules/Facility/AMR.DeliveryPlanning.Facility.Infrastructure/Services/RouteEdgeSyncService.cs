using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Services;

/// <summary>
/// Periodically pulls route costs from RIOT3 and upserts them into facility.RouteEdges.
/// Only syncs Maps and Stations that have VendorRef set.
/// Runs at startup (after a short delay) then on the configured interval.
/// </summary>
public sealed class RouteEdgeSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRiot3RouteClient _riot3Client;
    private readonly ILogger<RouteEdgeSyncService> _logger;
    private readonly TimeSpan _interval;

    // Limit concurrent RIOT3 HTTP calls to avoid overwhelming the vendor API
    private static readonly SemaphoreSlim _concurrencyGate = new(5, 5);

    public RouteEdgeSyncService(
        IServiceScopeFactory scopeFactory,
        IRiot3RouteClient riot3Client,
        ILogger<RouteEdgeSyncService> logger,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _riot3Client = riot3Client;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Short startup delay — wait for DB to be ready
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
            _logger.LogDebug("RouteEdgeSyncService: no maps with VendorRef — skipping sync");
            return;
        }

        _logger.LogInformation("RouteEdgeSyncService: syncing route edges for {Count} maps", maps.Count);

        foreach (var map in maps)
        {
            if (ct.IsCancellationRequested) break;
            await SyncMapAsync(db, map, ct);
        }
    }

    private async Task SyncMapAsync(FacilityDbContext db, Map map, CancellationToken ct)
    {
        var stations = await db.Stations
            .Where(s => s.MapId == map.Id && s.VendorRef != null)
            .ToListAsync(ct);

        if (stations.Count == 0)
        {
            _logger.LogDebug("RouteEdgeSyncService: map {MapId} ({Name}) has no stations with VendorRef — skipping",
                map.Id, map.Name);
            return;
        }

        // Build lookup: RIOT3 stationVendorRef → our Station.Id
        var stationByVendorRef = stations
            .Where(s => s.VendorRef != null)
            .ToDictionary(s => s.VendorRef!, s => s.Id);

        // Fetch all route costs from RIOT3 concurrently (bounded by semaphore)
        var fetchTasks = stations.Select(s => FetchCostsAsync(map.VendorRef!, s.VendorRef!, ct));
        var results = await Task.WhenAll(fetchTasks);

        // Build new edges, deduplicating (lower cost wins for duplicate pairs)
        var edgeMap = new Dictionary<(Guid, Guid), (double cost, double distance)>();
        foreach (var response in results.Where(r => r != null))
        {
            if (!stationByVendorRef.TryGetValue(response!.StationId, out var fromId)) continue;

            foreach (var entry in response.Costs)
            {
                if (!stationByVendorRef.TryGetValue(entry.TargetStationId, out var toId)) continue;
                if (fromId == toId) continue;

                var key = (fromId, toId);
                if (!edgeMap.TryGetValue(key, out var existing) || entry.Cost < existing.cost)
                    edgeMap[key] = (entry.Cost, entry.Distance);
            }
        }

        if (edgeMap.Count == 0)
        {
            _logger.LogWarning(
                "RouteEdgeSyncService: map {MapId} ({Name}) — RIOT3 returned no usable edges. " +
                "Check that Stations have correct VendorRef values.",
                map.Id, map.Name);
            return;
        }

        // Replace all edges for this map atomically
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var existing = await db.RouteEdges.Where(e => e.MapId == map.Id).ToListAsync(ct);
            db.RouteEdges.RemoveRange(existing);

            var newEdges = edgeMap.Select(kv =>
                new RouteEdge(Guid.NewGuid(), map.Id, kv.Key.Item1, kv.Key.Item2,
                    kv.Value.distance, kv.Value.cost, isBidirectional: false));
            await db.RouteEdges.AddRangeAsync(newEdges, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "RouteEdgeSyncService: map {MapId} ({Name}) — synced {Count} edges (replaced {Old})",
                map.Id, map.Name, edgeMap.Count, existing.Count);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "RouteEdgeSyncService: failed to save edges for map {MapId}", map.Id);
        }
    }

    private async Task<Riot3RouteCostResponse?> FetchCostsAsync(
        string mapVendorRef, string stationVendorRef, CancellationToken ct)
    {
        await _concurrencyGate.WaitAsync(ct);
        try
        {
            return await _riot3Client.GetRouteCostsAsync(mapVendorRef, stationVendorRef, ct);
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }
}
