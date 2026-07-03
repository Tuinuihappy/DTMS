using DTMS.Wms.Application.Commands.SyncWmsLocations;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.Wms.Infrastructure.Services;

/// <summary>
/// Periodic poller that dispatches <see cref="SyncWmsLocationsCommand"/> at
/// a configurable cadence. Mirror of <c>MapStationSyncService</c> in
/// Facility — same graceful-shutdown handling, per-cycle exception
/// isolation, and cycle-skip on host cancellation.
///
/// Only runs when <see cref="WmsOptions.Enabled"/> is true — a fresh
/// deployment with no token doesn't hammer <c>10.204.212.28</c> with
/// silent 401s.
/// </summary>
public sealed class WmsLocationSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<WmsOptions> _options;
    private readonly ILogger<WmsLocationSyncService> _logger;

    public WmsLocationSyncService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<WmsOptions> options,
        ILogger<WmsLocationSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup delay lines up with the rest of the vendor pollers so
        // the api container has time to finish DI + migration checks
        // before hitting external systems.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = _options.CurrentValue;

            if (!opts.Enabled)
            {
                _logger.LogDebug("[WmsSync] Disabled via config — skipping cycle.");
            }
            else
            {
                try
                {
                    await RunOneCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[WmsSync] Sync cycle failed — will retry next interval.");
                }
            }

            var interval = TimeSpan.FromSeconds(Math.Max(30, opts.SyncIntervalSeconds));
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunOneCycleAsync(CancellationToken ct)
    {
        // Fresh scope per cycle — DbContext lifecycle stays bounded even
        // if the sync command runs a while.
        using var scope = _scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(new SyncWmsLocationsCommand(), ct);
        if (result.IsFailure)
        {
            _logger.LogWarning("[WmsSync] Cycle returned Failure — {Error}", result.Error);
        }
    }
}
