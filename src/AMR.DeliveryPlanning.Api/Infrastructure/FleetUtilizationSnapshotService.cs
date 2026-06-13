using AMR.DeliveryPlanning.Fleet.Application.Projections;

namespace AMR.DeliveryPlanning.Api.Infrastructure;

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
    private readonly ILogger<FleetUtilizationSnapshotService> _logger;

    public FleetUtilizationSnapshotService(
        IServiceScopeFactory scopeFactory,
        ILogger<FleetUtilizationSnapshotService> logger)
    {
        _scopeFactory = scopeFactory;
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
