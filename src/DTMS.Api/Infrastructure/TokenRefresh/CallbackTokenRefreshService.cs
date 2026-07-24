using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Application.Repositories;
using Microsoft.Extensions.Options;

namespace DTMS.Api.Infrastructure.TokenRefresh;

/// <summary>
/// Background loop that keeps outbound callback tokens fresh. Each sweep loads
/// the systems that have auto-refresh configured and runs each through the
/// shared <see cref="ICallbackTokenRefresher"/> (which owns the per-system lock,
/// due/perpetual guards, mint, persist and cache-invalidation).
///
/// <para>Registration is gated by <c>Workers:CallbackTokenRefresh:RunInThisProcess</c>
/// so only one process role runs the loop; the manual "refresh now" endpoint
/// uses the same refresher and works on every tier. Minting is bounded by
/// <c>MaxParallelism</c> with a little jitter so a sweep doesn't hit every mint
/// endpoint at once.</para>
/// </summary>
public sealed class CallbackTokenRefreshService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<CallbackTokenRefreshOptions> _options;
    private readonly ILogger<CallbackTokenRefreshService> _logger;

    public CallbackTokenRefreshService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<CallbackTokenRefreshOptions> options,
        ILogger<CallbackTokenRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // No global on/off flag — the loop runs wherever the process-role gate
        // (Workers:CallbackTokenRefresh:RunInThisProcess) registered it, and each
        // system's own "enabled" flag (the UI checkbox, stored in
        // TokenRefreshConfig) decides whether that system is actually refreshed.
        // A sweep over zero enabled systems is a cheap no-op.
        _logger.LogInformation("Callback token refresh loop started");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.StartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Callback token refresh sweep threw — will retry next tick");
            }

            var interval = TimeSpan.FromSeconds(Math.Max(1, _options.CurrentValue.PollIntervalSeconds));
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISystemCredentialRepository>();
        var refresher = scope.ServiceProvider.GetRequiredService<ICallbackTokenRefresher>();

        var keys = await repo.ListKeysWithTokenRefreshAsync(ct);
        if (keys.Count == 0) return;

        var opts = _options.CurrentValue;
        using var gate = new SemaphoreSlim(Math.Max(1, opts.MaxParallelism));
        // Deterministic per-key jitter (0–500ms) spreads the mint calls without
        // needing shared RNG state — hash the key into the window.
        var tasks = keys.Select(async key =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var jitterMs = (uint)key.GetHashCode() % 500;
                await Task.Delay(TimeSpan.FromMilliseconds(jitterMs), ct);
                var result = await refresher.RefreshAsync(key, force: false, ct);
                if (result.Outcome is RefreshOutcome.Refreshed or RefreshOutcome.Failed or RefreshOutcome.Rejected)
                    _logger.LogInformation(
                        "Token refresh system={System} → {Outcome} {Message}",
                        key, result.Outcome, result.Message);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }
}
