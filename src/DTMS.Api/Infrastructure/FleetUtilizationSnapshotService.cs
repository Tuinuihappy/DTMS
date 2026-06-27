using DTMS.Api.Realtime.Pipeline;
using DTMS.Fleet.Application.Projections;

namespace DTMS.Api.Infrastructure;

/// <summary>
/// Phase P3.2 — Hosted background service that refreshes the current
/// hour's <c>FleetUtilizationHourly</c> row every minute. Runs idle if
/// the writer throws (logged, no rethrow) so a transient DB blip doesn't
/// take down the whole host.
/// </summary>
public class FleetUtilizationSnapshotService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    // Delay the first tick a bit so the API has time to settle (migrations,
    // initial DI scope) before the snapshot writer hits the DB.
    private static readonly TimeSpan WarmupDelay = TimeSpan.FromSeconds(20);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DashboardCounterBatcher _batcher;
    private readonly ILogger<FleetUtilizationSnapshotService> _logger;

    public FleetUtilizationSnapshotService(
        IServiceScopeFactory scopeFactory,
        DashboardCounterBatcher batcher,
        ILogger<FleetUtilizationSnapshotService> logger)
    {
        _scopeFactory = scopeFactory;
        _batcher = batcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FleetUtilizationSnapshotService started (tick {Interval})", TickInterval);

        try
        {
            await Task.Delay(WarmupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var writer = scope.ServiceProvider.GetRequiredService<IFleetUtilizationSnapshotWriter>();
                await writer.UpsertCurrentBucketAsync(stoppingToken);

                // Phase P3.x — hint the "fleet" dashboard board after each
                // successful upsert. Batcher coalesces hints in its 250 ms
                // window so 2-3 concurrent ticks (shouldn't happen, but defensively
                // OK) become one CountersUpdated push.
                await _batcher.Enqueue(
                    "fleet",
                    new { kind = "fleet-utilization.bucket-touched", snapshotAtUtc = DateTime.UtcNow });
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fleet utilization snapshot failed — will retry next tick");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }
}
