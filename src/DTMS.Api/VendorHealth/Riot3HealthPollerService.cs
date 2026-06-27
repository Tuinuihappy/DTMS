using Microsoft.Extensions.Options;

namespace DTMS.Api.VendorHealth;

public sealed class Riot3HealthPollerService : BackgroundService
{
    private const string VendorName = "riot3";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVendorHealthStore _store;
    private readonly IOptionsMonitor<VendorHealthOptions> _options;
    private readonly ILogger<Riot3HealthPollerService> _logger;

    public Riot3HealthPollerService(
        IServiceScopeFactory scopeFactory,
        IVendorHealthStore store,
        IOptionsMonitor<VendorHealthOptions> options,
        ILogger<Riot3HealthPollerService> logger)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue.Riot3;
        if (!opts.Enabled)
        {
            _logger.LogInformation("RIOT3 health poller disabled via config");
            return;
        }

        // Seed an Unknown snapshot so /health/vendors has something to read
        // before the first probe completes.
        _store.Update(VendorHealthSnapshot.Initial(VendorName, DateTime.UtcNow));

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
                _logger.LogError(ex, "RIOT3 health poll cycle threw — will retry next tick");
            }

            var interval = TimeSpan.FromSeconds(Math.Max(1, _options.CurrentValue.Riot3.PollIntervalSeconds));
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
        var probe = scope.ServiceProvider.GetRequiredService<IRiot3HealthProbe>();

        var opts = _options.CurrentValue.Riot3;
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds)));

        var outcome = await probe.ProbeAsync(probeCts.Token);
        var previous = _store.Get(VendorName);
        var next = VendorHealthStateMachine.Reduce(previous, outcome, opts, DateTime.UtcNow);
        _store.Update(next);

        if (previous?.Status != next.Status)
        {
            _logger.LogInformation(
                "RIOT3 health status changed: {Previous} → {Next} (outcome={OutcomeKind}, code={Code}, reason={Reason}, latency={LatencyMs}ms)",
                previous?.Status, next.Status, outcome.Kind, outcome.Code, outcome.FailureReason, outcome.LatencyMs);
        }
    }
}
