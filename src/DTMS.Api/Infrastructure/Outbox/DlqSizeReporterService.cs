using DTMS.SharedKernel.Diagnostics;
using DTMS.SharedKernel.Outbox;

namespace DTMS.Api.Infrastructure.Outbox;

/// <summary>
/// Phase O3 — periodic reporter for the DLQ size gauge. Ticks every
/// <see cref="TickInterval"/> and asks <see cref="IDeadLetterStore.CountAsync"/>
/// for the current size, updating the <c>dtms.workflow.outbox_dlq_size</c>
/// observable gauge on <see cref="WorkflowMetrics"/>.
///
/// <para>Kept as a separate hosted service (rather than piggy-backing on
/// OutboxProcessorService) so it works even in poll-only postures and
/// so ops can disable the counter without also killing the drain.</para>
/// </summary>
public sealed class DlqSizeReporterService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkflowMetrics _metrics;
    private readonly ILogger<DlqSizeReporterService> _log;

    public DlqSizeReporterService(
        IServiceScopeFactory scopeFactory,
        WorkflowMetrics metrics,
        ILogger<DlqSizeReporterService> log)
    {
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("DlqSizeReporterService started (tick {Seconds}s)", TickInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dlq = scope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
                var count = await dlq.CountAsync(stoppingToken);
                _metrics.SetOutboxDlqSize(count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "DlqSizeReporterService tick failed — will retry");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
