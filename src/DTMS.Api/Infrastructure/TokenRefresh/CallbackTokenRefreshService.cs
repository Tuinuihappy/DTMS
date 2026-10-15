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
            var anyFailed = false;
            try
            {
                anyFailed = await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Listing the systems failed (DB down) — no system got a try.
                _logger.LogError(ex, "Callback token refresh sweep threw before refreshing — retrying soon");
                anyFailed = true;
            }

            var interval = NextDelay(_options.CurrentValue, anyFailed);
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Wait before the next sweep: the normal interval, or the shorter retry
    /// delay when this sweep had a failure (never longer than the interval).
    /// </summary>
    internal static TimeSpan NextDelay(CallbackTokenRefreshOptions opts, bool anyFailed)
    {
        var poll = Math.Max(1, opts.PollIntervalSeconds);
        var seconds = anyFailed ? Math.Clamp(opts.RetryAfterFailureSeconds, 1, poll) : poll;
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Refreshes every system due; returns true when any of them failed.</summary>
    internal async Task<bool> SweepOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<string> keys;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISystemCredentialRepository>();
            keys = (await repo.ListKeysWithTokenRefreshAsync(ct)).ToList();
        }
        if (keys.Count == 0) return false;

        var opts = _options.CurrentValue;
        using var gate = new SemaphoreSlim(Math.Max(1, opts.MaxParallelism));
        var tasks = keys.Select(async key =>
        {
            await gate.WaitAsync(ct);   // outside the try: a cancelled wait holds no slot to release
            try
            {
                // Per-key jitter (0–500 ms) so a sweep doesn't hit every mint
                // endpoint in the same instant.
                await Task.Delay(TimeSpan.FromMilliseconds(Jitter(key)), ct);

                // One scope — so one refresher and one DbContext — per system.
                // A DbContext isn't safe for concurrent use; sharing one across
                // these parallel refreshes threw "A second operation was started
                // on this context instance" and dropped a system's refresh most
                // hours (both are due every sweep).
                using var scope = _scopeFactory.CreateScope();
                var refresher = scope.ServiceProvider.GetRequiredService<ICallbackTokenRefresher>();
                var result = await refresher.RefreshAsync(key, force: false, ct);
                if (result.Outcome is RefreshOutcome.Refreshed or RefreshOutcome.Failed or RefreshOutcome.Rejected)
                    _logger.LogInformation(
                        "Token refresh system={System} → {Outcome} {Message}",
                        key, result.Outcome, result.Message);
                return result.Outcome == RefreshOutcome.Failed;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Isolated per system: one failing doesn't stop the others.
                _logger.LogWarning(ex, "Token refresh system={System} failed — retrying soon", key);
                return true;
            }
            finally
            {
                gate.Release();
            }
        });

        var failed = await Task.WhenAll(tasks);
        return failed.Any(f => f);
    }

    // Stable across restarts, unlike string.GetHashCode (randomised per process).
    private static int Jitter(string key)
    {
        var h = 0;
        foreach (var c in key) h = (h * 31 + c) & 0x7fffffff;
        return h % 500;
    }
}
