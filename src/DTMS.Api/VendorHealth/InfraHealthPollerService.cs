using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using FxHealthStatus = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;

namespace DTMS.Api.VendorHealth;

/// <summary>
/// Polls the registered "ready"-tagged IHealthChecks (postgres / redis /
/// rabbitmq / masstransit-bus) on a cadence and feeds each entry through
/// the same <see cref="VendorHealthStateMachine"/> + store that RIOT3
/// uses. This gives infra components the same debounced status, latency,
/// streak, last-changed timestamps, and SignalR push notifications that
/// external vendors already have — the frontend renders both with the
/// same card.
///
/// Vendor names are prefixed (default "infra:") so the UI can split
/// infra cards from external vendors without a schema field.
/// </summary>
public sealed class InfraHealthPollerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVendorHealthStore _store;
    private readonly IOptionsMonitor<VendorHealthOptions> _options;
    private readonly ILogger<InfraHealthPollerService> _logger;

    public InfraHealthPollerService(
        IServiceScopeFactory scopeFactory,
        IVendorHealthStore store,
        IOptionsMonitor<VendorHealthOptions> options,
        ILogger<InfraHealthPollerService> logger)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue.Infra;
        if (!opts.Enabled)
        {
            _logger.LogInformation("Infrastructure health poller disabled via config");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(opts.StartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Infra health poll cycle threw — will retry next tick");
            }

            var interval = TimeSpan.FromSeconds(Math.Max(1, _options.CurrentValue.Infra.PollIntervalSeconds));
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var healthChecks = scope.ServiceProvider.GetRequiredService<HealthCheckService>();

        var opts = _options.CurrentValue.Infra;
        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        pollCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds)));

        var report = await healthChecks.CheckHealthAsync(
            check => check.Tags.Contains("ready"),
            pollCts.Token);

        // State machine reuses Riot3HealthOptions for thresholds — synthesise
        // a Riot3HealthOptions from the infra config so we don't have to
        // generalise the state machine signature.
        var stateOptions = new Riot3HealthOptions
        {
            FailureThreshold = opts.FailureThreshold,
            RecoveryThreshold = opts.RecoveryThreshold,
        };
        var now = DateTime.UtcNow;

        foreach (var (name, entry) in report.Entries)
        {
            var vendorKey = opts.VendorNamePrefix + name;
            var outcome = ToOutcome(entry);
            var previous = _store.Get(vendorKey) ?? VendorHealthSnapshot.Initial(vendorKey, now);
            var next = VendorHealthStateMachine.Reduce(previous, outcome, stateOptions, now);
            _store.Update(next);

            if (previous.Status != next.Status)
            {
                _logger.LogInformation(
                    "Infra health status changed: {Vendor} {Previous} → {Next} (outcome={OutcomeKind}, reason={Reason}, latency={LatencyMs}ms)",
                    vendorKey, previous.Status, next.Status, outcome.Kind, outcome.FailureReason, outcome.LatencyMs);
            }
        }
    }

    private static ProbeOutcome ToOutcome(HealthReportEntry entry)
    {
        var latency = (int)entry.Duration.TotalMilliseconds;

        return entry.Status switch
        {
            FxHealthStatus.Healthy => new ProbeOutcome(
                ProbeOutcomeKind.Success,
                Code: null,
                Message: entry.Description,
                LatencyMs: latency,
                FailureReason: null),

            // Degraded — service responding but reports impaired state.
            // Maps to Auth outcome which the state machine treats as an
            // instant "Degraded" status (no debounce). The probe outcome
            // carries the original description so the UI shows the right
            // text, not "auth issue".
            FxHealthStatus.Degraded => new ProbeOutcome(
                ProbeOutcomeKind.Auth,
                Code: null,
                Message: entry.Description,
                LatencyMs: latency,
                FailureReason: entry.Description ?? entry.Exception?.Message ?? "Degraded"),

            // Unhealthy — failed check. Goes through the 3-fail debounce
            // before flipping to Unhealthy status.
            _ => new ProbeOutcome(
                ProbeOutcomeKind.Failure,
                Code: null,
                Message: entry.Description,
                LatencyMs: latency,
                FailureReason: entry.Exception?.Message
                    ?? entry.Description
                    ?? entry.Status.ToString()),
        };
    }
}
