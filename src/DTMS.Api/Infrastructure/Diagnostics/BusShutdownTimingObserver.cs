using DTMS.SharedKernel.Diagnostics;
using MassTransit;

namespace DTMS.Api.Infrastructure.Diagnostics;

// G2 (crash-recovery-workflow-resilience-plan.md §11) — measures the
// "bus" phase of host shutdown: time MassTransit spends draining its
// in-flight consume operations before the bus instance reports stopped.
//
// MassTransit invokes IBusObserver.PreStop right before it begins the
// internal stop sequence (waiting for in-flight messages up to its
// configured StopTimeout, currently 45s per MassTransitHostOptions),
// and PostStop once the bus fully reports stopped. The delta is the
// "bus" phase ops asked about — typically the dominant chunk of the
// 49s+ baseline shutdown time when there are in-flight consumes.
//
// All other observer methods are no-ops; we only care about stop timing.
// Failures are logged but never thrown — instrumentation must not break
// the underlying shutdown sequence.
public sealed class BusShutdownTimingObserver : IBusObserver
{
    private readonly WorkflowMetrics _metrics;
    private readonly ILogger<BusShutdownTimingObserver> _logger;
    private long _preStopTicks;

    public BusShutdownTimingObserver(
        WorkflowMetrics metrics,
        ILogger<BusShutdownTimingObserver> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    public Task PreStop(IBus bus)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        Interlocked.Exchange(ref _preStopTicks, nowTicks);
        _logger.LogWarning(
            "[Shutdown] MassTransit PreStop at {Timestamp:o} — bus begins draining in-flight consumes",
            new DateTime(nowTicks, DateTimeKind.Utc));
        return Task.CompletedTask;
    }

    public Task PostStop(IBus bus)
    {
        var startTicks = Interlocked.Read(ref _preStopTicks);
        if (startTicks == 0)
        {
            _logger.LogWarning("[Shutdown] MassTransit PostStop without PreStop timestamp — skipping bus phase metric");
            return Task.CompletedTask;
        }
        var sec = (DateTime.UtcNow - new DateTime(startTicks, DateTimeKind.Utc)).TotalSeconds;
        _metrics.RecordShutdownDuration("bus", sec);
        _logger.LogWarning(
            "[Shutdown] MassTransit PostStop — bus drain took {Seconds:0.00}s",
            sec);
        return Task.CompletedTask;
    }

    // Other IBusObserver methods — no-ops. MassTransit requires them but
    // they're not relevant to shutdown-phase observability.
    public void PostCreate(IBus bus) { }
    public void CreateFaulted(Exception exception) { }
    public Task PreStart(IBus bus) => Task.CompletedTask;
    public Task PostStart(IBus bus, Task<BusReady> busReady) => Task.CompletedTask;
    public Task StartFaulted(IBus bus, Exception exception) => Task.CompletedTask;
    public Task StopFaulted(IBus bus, Exception exception) => Task.CompletedTask;
}
