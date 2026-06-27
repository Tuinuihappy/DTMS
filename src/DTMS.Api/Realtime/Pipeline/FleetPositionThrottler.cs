using System.Collections.Concurrent;
using DTMS.Api.Realtime.Hubs;
using DTMS.Api.Realtime.Hubs.Clients;
using Microsoft.AspNetCore.SignalR;

namespace DTMS.Api.Realtime.Pipeline;

/// <summary>
/// Throttle robot position updates so high-frequency upstream signals
/// (RIOT3 webhook every ~100 ms, robot poller every second) never
/// translate into &gt; 1 push/sec per facility to the browser. Latest-wins
/// per robot — only the most recent <see cref="object"/> position for each
/// (floor, robot) pair survives within the 1-second window.
///
/// Concurrency model:
///   - <c>_pending[floor][robot] = latestPosition</c> overwrite is cheap
///     (ConcurrentDictionary.AddOrUpdate)
///   - One drain timer per process; per-floor groups dispatched in
///     parallel inside the loop using <c>Task.WhenAll</c>.
/// </summary>
public sealed class FleetPositionThrottler : BackgroundService
{
    private static readonly TimeSpan FlushWindow = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, object>> _pending = new();
    private readonly IHubContext<FleetHub, IFleetClient> _hub;
    private readonly ILogger<FleetPositionThrottler> _logger;

    public FleetPositionThrottler(
        IHubContext<FleetHub, IFleetClient> hub,
        ILogger<FleetPositionThrottler> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// Upstream callers (RIOT3 poller, vendor webhook consumers) call
    /// this whenever a new robot position arrives. Latest-wins — older
    /// positions for the same robot within the 1-second window are
    /// overwritten before they ever leave the process.
    /// </summary>
    public void Enqueue(Guid facilityId, Guid robotId, object position)
    {
        var bucket = _pending.GetOrAdd(facilityId, _ => new ConcurrentDictionary<Guid, object>());
        bucket[robotId] = position;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "FleetPositionThrottler started — flushing every {WindowMs} ms",
            FlushWindow.TotalMilliseconds);

        using var timer = new PeriodicTimer(FlushWindow);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Capture per-floor snapshots, clearing each bucket as we go
            // so concurrent Enqueue calls during the flush race correctly
            // into the NEXT window — not lost.
            var snapshots = new List<(Guid FacilityId, IReadOnlyList<object> Positions)>(
                capacity: _pending.Count);
            foreach (var (facilityId, bucket) in _pending)
            {
                if (bucket.IsEmpty) continue;
                var positions = new List<object>(bucket.Count);
                foreach (var key in bucket.Keys.ToList())
                {
                    if (bucket.TryRemove(key, out var value))
                        positions.Add(value);
                }
                if (positions.Count > 0)
                    snapshots.Add((facilityId, positions));
            }

            if (snapshots.Count == 0) continue;

            var tasks = new List<Task>(snapshots.Count);
            foreach (var (facilityId, positions) in snapshots)
            {
                tasks.Add(PushAsync(facilityId, positions));
            }
            await Task.WhenAll(tasks);
        }
    }

    private async Task PushAsync(Guid facilityId, IReadOnlyList<object> positions)
    {
        try
        {
            await _hub.Clients
                .Group(FleetHub.FloorGroupKey(facilityId))
                .RobotPositionsUpdated(positions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to push {Count} robot positions to floor {FacilityId}",
                positions.Count, facilityId);
        }
    }
}
