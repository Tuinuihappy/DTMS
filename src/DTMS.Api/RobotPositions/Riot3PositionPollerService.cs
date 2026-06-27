using DTMS.Facility.Infrastructure.Data;
using DTMS.Fleet.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Api.RobotPositions;

/// <summary>
/// Polls RIOT3 once per second for the live robot snapshot and reconciles it
/// into <see cref="IRobotPositionStore"/>. One BackgroundService → one RIOT3
/// poll per cycle, regardless of how many frontends are watching the map page.
///
/// Maintains a tiny cache of (RIOT3 mapId int → DTMS Map.Id Guid) so the map
/// filter on the read path is a straight equality check. Cache is refreshed
/// when the poll encounters a new RIOT3 mapId.
/// </summary>
public sealed class Riot3PositionPollerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRobotPositionStore _store;
    private readonly IRiot3FleetClient _client;
    private readonly ILogger<Riot3PositionPollerService> _logger;
    private readonly TimeSpan _interval;

    private readonly Dictionary<int, Guid> _mapLookup = new();

    public Riot3PositionPollerService(
        IServiceScopeFactory scopeFactory,
        IRobotPositionStore store,
        IRiot3FleetClient client,
        ILogger<Riot3PositionPollerService> logger,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _client = client;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief delay so the API is fully up before we hit RIOT3 from a host
        // thread — mirrors MapStationSyncService for the same reason.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RIOT3 position poll cycle threw — will retry next tick");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var live = await _client.GetRobotLivePositionsAsync(ct);
        if (live.Count == 0)
        {
            // Empty list = either no robots or transport failed. Treat as
            // "no live data" — clear the store so the UI doesn't keep stale
            // dots floating around. The poller will re-fill on the next OK
            // response.
            _store.ReplaceAll([]);
            return;
        }

        // Resolve any unknown RIOT3 mapId values in one round-trip so we
        // don't open a DbContext per robot.
        var unknownVendorIds = live
            .Where(r => r.MapId.HasValue && !_mapLookup.ContainsKey(r.MapId.Value))
            .Select(r => r.MapId!.Value)
            .Distinct()
            .ToList();

        if (unknownVendorIds.Count > 0)
        {
            await RefreshMapLookupAsync(unknownVendorIds, ct);
        }

        var nowUtc = DateTime.UtcNow;
        var dtos = live
            .Select(r =>
            {
                if (!r.MapId.HasValue) return null;
                if (!_mapLookup.TryGetValue(r.MapId.Value, out var dtmsMapId)) return null;
                return new RobotPositionDto(
                    DeviceKey: r.DeviceKey,
                    DeviceName: r.DeviceName,
                    MapId: dtmsMapId,
                    VendorMapId: r.MapId.Value,
                    // Raw coords — RIOT3 may return negative x/y depending on
                    // the map's origin choice. The canvas viewBox is computed
                    // from min/max of all rendered points, so it copes with
                    // any quadrant. Flipping sign here would break the spatial
                    // relationship between robots and stations when either
                    // crosses the origin.
                    X: r.X,
                    Y: r.Y,
                    // Robot pose theta is raw radians (RIOT3 doc example:
                    // "theta": 6.278 corresponds to "angleInDegrees": 359.7).
                    // Station yaw is the one in milli-radians; pose theta isn't.
                    Theta: r.Theta,
                    SystemState: r.SystemState,
                    ConnectionState: r.ConnectionState,
                    Emergency: r.Emergency,
                    Paused: r.Paused,
                    BatteryPercentage: r.BatteryPercentage,
                    Charging: r.Charging,
                    OrderKey: r.OrderKey,
                    OrderName: r.OrderName,
                    StartToEnd: r.StartToEnd,
                    StationName: r.StationName,
                    UpdatedAtUtc: nowUtc);
            })
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();

        var retained = _store.ReplaceAll(dtos);
        _logger.LogDebug(
            "Riot3PositionPoller: {Live} robots from RIOT3, {Retained} mapped into store",
            live.Count, retained);
    }

    private async Task RefreshMapLookupAsync(IEnumerable<int> vendorIds, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();

        var vendorRefs = vendorIds.Select(v => v.ToString()).ToList();

        var rows = await db.Maps
            .Where(m => m.VendorRef != null && vendorRefs.Contains(m.VendorRef))
            .Select(m => new { m.Id, m.VendorRef })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            if (int.TryParse(row.VendorRef, out var intRef))
                _mapLookup[intRef] = row.Id;
        }
    }
}
