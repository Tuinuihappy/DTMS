using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Api.Infrastructure.Metrics;

/// <summary>
/// WMS PR-4b (PR-H) — Background job that refreshes the
/// <see cref="PoolMetrics"/> depth gauge every 10 seconds by counting
/// pooled trips against the partial index <c>IX_Trips_Pool</c>.
///
/// Why polling over event-driven? The gauge answers "how many trips are
/// available RIGHT NOW", which the DDD event stream can't cheaply
/// materialize (Added/Removed/Claimed events across many outbox rows
/// per tick). A single indexed COUNT is ~1 ms on the current dataset.
///
/// Exceptions inside the tick are logged + swallowed — a transient
/// DB blip must not kill the service (the next tick just resumes).
/// </summary>
public sealed class PoolDepthPollingService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PoolMetrics _metrics;
    private readonly ILogger<PoolDepthPollingService> _logger;

    public PoolDepthPollingService(
        IServiceScopeFactory scopeFactory,
        PoolMetrics metrics,
        ILogger<PoolDepthPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the migrator + DB pool time to warm up before the first tick.
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation(
            "[PoolDepthPolling] started (interval={IntervalSeconds}s)",
            Interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DispatchDbContext>();
                var depth = await db.Trips
                    .AsNoTracking()
                    .Where(t => t.Status == TripStatus.Created
                             && t.DispatchedAt != null
                             && t.ClaimedByOperatorId == null)
                    .LongCountAsync(stoppingToken);
                _metrics.SetDepth(depth);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[PoolDepthPolling] tick failed — next tick will retry");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
