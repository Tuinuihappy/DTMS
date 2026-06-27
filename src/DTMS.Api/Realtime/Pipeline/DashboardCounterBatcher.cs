using System.Collections.Concurrent;
using System.Threading.Channels;
using DTMS.Api.Realtime.Hubs;
using DTMS.Api.Realtime.Hubs.Clients;
using Microsoft.AspNetCore.SignalR;

namespace DTMS.Api.Realtime.Pipeline;

/// <summary>
/// Buffers dashboard counter deltas in memory and flushes them to the
/// SignalR <see cref="DashboardHub"/> every 250 ms — so even when 100+
/// status transitions per second occur during stress windows the chart
/// re-render rate stays bounded to ~4 Hz per board.
///
/// Wire-up:
///   Projector → <see cref="Enqueue"/>(boardKey, delta) → channel
///                                                       │
///   Every 250 ms ─────────────────────────────────────────┘
///   drain channel → group by boardKey → push CountersUpdated(list)
///
/// Why a single batcher across all boards?
///   The 250 ms window is shared — one timer wakes up, drains once,
///   fans out to each board's group. Avoids N timers for N boards.
/// </summary>
public sealed class DashboardCounterBatcher : BackgroundService
{
    private static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(250);

    private readonly Channel<DashboardDelta> _channel =
        Channel.CreateUnbounded<DashboardDelta>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    private readonly IHubContext<DashboardHub, IDashboardClient> _hub;
    private readonly ILogger<DashboardCounterBatcher> _logger;

    public DashboardCounterBatcher(
        IHubContext<DashboardHub, IDashboardClient> hub,
        ILogger<DashboardCounterBatcher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// Fire-and-forget enqueue. Returns ValueTask so callers can await
    /// at zero cost in the synchronous path (unbounded channel never
    /// blocks). Drops the delta silently if writer fails (e.g. shutdown)
    /// — projectors should not be aborted because the dashboard is being
    /// torn down.
    /// </summary>
    public ValueTask Enqueue(string boardKey, object delta)
    {
        if (string.IsNullOrWhiteSpace(boardKey)) return ValueTask.CompletedTask;
        return _channel.Writer.WriteAsync(new DashboardDelta(boardKey, delta));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DashboardCounterBatcher started — flushing every {WindowMs} ms",
            BatchWindow.TotalMilliseconds);

        using var timer = new PeriodicTimer(BatchWindow);
        var grouped = new Dictionary<string, List<object>>(capacity: 8);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Drain everything queued since the previous tick.
            grouped.Clear();
            while (_channel.Reader.TryRead(out var item))
            {
                if (!grouped.TryGetValue(item.BoardKey, out var list))
                    grouped[item.BoardKey] = list = new List<object>();
                list.Add(item.Delta);
            }

            if (grouped.Count == 0) continue;

            foreach (var (boardKey, list) in grouped)
            {
                try
                {
                    await _hub.Clients
                        .Group(DashboardHub.GroupKey(boardKey))
                        .CountersUpdated(list);
                }
                catch (Exception ex)
                {
                    // Don't crash the batcher — log and keep draining.
                    // A bad subscriber shouldn't take down the whole pipe.
                    _logger.LogWarning(ex,
                        "Failed to push {Count} deltas to dashboard group {BoardKey}",
                        list.Count, boardKey);
                }
            }
        }
    }

    private readonly record struct DashboardDelta(string BoardKey, object Delta);
}
