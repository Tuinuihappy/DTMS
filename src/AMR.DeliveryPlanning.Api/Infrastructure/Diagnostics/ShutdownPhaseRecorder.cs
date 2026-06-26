using DTMS.SharedKernel.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace AMR.DeliveryPlanning.Api.Infrastructure.Diagnostics;

// G2 (crash-recovery-workflow-resilience-plan.md §11) — records the
// "total" shutdown phase metric + writes structured log lines so ops
// can answer "what did the 49s actually go to?" without grepping
// ApplicationStopping / Stopped logs across many sources.
//
// Total measurement uses IHostApplicationLifetime cancellation tokens —
// the cleanest place to bracket the full host stop sequence:
//   ApplicationStopping  → first cancellation; signals SIGTERM received,
//                          IHostedService.StopAsync about to begin
//   ApplicationStopped   → last cancellation; all hosted services
//                          finished StopAsync, host is about to exit
//
// `bus` and `hosted_services` phases come from peers:
//   - BusShutdownTimingObserver records `bus`
//   - hosted_services derived later in Phase G dashboard arithmetic
//     (total - bus = approx hosted-services drain); not recorded here
//     to avoid leaky time-source coupling
//
// Phase 2 (deferred to Phase G observability sprint): per-service
// breakdown via IHostedService decoration. Today's MVP gives the headline
// number every ops dashboard needs first.
public sealed class ShutdownPhaseRecorder : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly WorkflowMetrics _metrics;
    private readonly ILogger<ShutdownPhaseRecorder> _logger;

    // ApplicationStopping timestamp ticks (UTC). 0 = not yet stopping.
    // Read/written via Interlocked because Stopping fires on a different
    // thread from Stopped, and we want a happens-before guarantee.
    private long _stoppingTicks;

    public ShutdownPhaseRecorder(
        IHostApplicationLifetime lifetime,
        WorkflowMetrics metrics,
        ILogger<ShutdownPhaseRecorder> logger)
    {
        _lifetime = lifetime;
        _metrics = metrics;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime.ApplicationStopping.Register(() =>
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            Interlocked.Exchange(ref _stoppingTicks, nowTicks);
            _logger.LogWarning(
                "[Shutdown] ApplicationStopping fired at {Timestamp:o} — host begins draining hosted services",
                new DateTime(nowTicks, DateTimeKind.Utc));
        });

        _lifetime.ApplicationStopped.Register(() =>
        {
            var startTicks = Interlocked.Read(ref _stoppingTicks);
            if (startTicks == 0)
            {
                // Stopped without Stopping — process was killed before our
                // hook fired (rare; still log so we know it happened).
                _logger.LogWarning(
                    "[Shutdown] ApplicationStopped fired but no Stopping timestamp captured — process likely killed pre-graceful");
                return;
            }
            var totalSec = (DateTime.UtcNow - new DateTime(startTicks, DateTimeKind.Utc)).TotalSeconds;
            _metrics.RecordShutdownDuration("total", totalSec);
            _logger.LogWarning(
                "[Shutdown] ApplicationStopped — total {Seconds:0.00}s — host exiting",
                totalSec);
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
