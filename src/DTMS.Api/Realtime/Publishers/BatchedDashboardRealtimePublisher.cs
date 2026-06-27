using DTMS.Api.Realtime.Pipeline;
using DTMS.DeliveryOrder.Application.Projections;

namespace DTMS.Api.Realtime.Publishers;

/// <summary>
/// Composition-root realisation of <see cref="IDashboardRealtimePublisher"/>.
/// Forwards each "data changed" hint onto <see cref="DashboardCounterBatcher"/>
/// (P0.B11) so the existing 250 ms drain window bounds chart re-renders
/// across all subscribers.
///
/// Why this isn't a direct hub push:
///   - Hub push per event = unbounded fan-out under burst (status change
///     storms during ops cutover).
///   - Batcher coalesces multiple hints in the same window into ONE
///     CountersUpdated call per board — protects browser render budget
///     and the SignalR backplane.
/// </summary>
public sealed class BatchedDashboardRealtimePublisher : IDashboardRealtimePublisher
{
    // Board keys mirror the DashboardHub.Subscribe(boardKey) convention
    // from the SignalR hub catalog. Frontend pages pass one of these
    // strings when they subscribe — keep them in sync with docs/signalr-hub-catalog.md.
    private const string OrdersBoard = "orders";

    private readonly DashboardCounterBatcher _batcher;
    private readonly ILogger<BatchedDashboardRealtimePublisher> _logger;

    public BatchedDashboardRealtimePublisher(
        DashboardCounterBatcher batcher,
        ILogger<BatchedDashboardRealtimePublisher> logger)
    {
        _batcher = batcher;
        _logger = logger;
    }

    public async Task PublishOrderFunnelUpdatedAsync(
        DateTime bucketHourUtc,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Hint payload — frontend treats CountersUpdated entries as
            // refetch triggers (debounced), not deltas to merge. The
            // bucketHourUtc lets future client code narrow the refetch
            // window if the chart's current period falls outside.
            await _batcher.Enqueue(
                OrdersBoard,
                new { kind = "order-funnel.bucket-touched", bucketHourUtc });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to enqueue OrderFunnel hint for bucket {Bucket} — UI will catch up on next REST refresh",
                bucketHourUtc);
        }
    }
}
