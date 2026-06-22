// =============================================================================
// BACKGROUND SERVICE TEMPLATE
// =============================================================================
//
// Use this template for any IHostedService that needs to:
//   - Poll external systems (Riot3PositionPoller, Riot3Reconciliation)
//   - Watchdog for stale state (PlanningReconciliation, ManualTripSlaWatchdog)
//   - Periodic cleanup (OperatorPresenceCleanup, OrphanPodCleanup)
//   - Snapshot aggregation (FleetUtilizationSnapshot)
//   - Outbox dispatch (OutboxProcessor)
//
// Filename: src/Modules/{Module}/.../Application/BackgroundServices/{Name}Service.cs
//
// Reference examples (read these first):
//   src/AMR.DeliveryPlanning.Api/Infrastructure/Reconciliation/PlanningReconciliationService.cs
//     ↑ canonical pattern: hot-reload options, scoped per-tick, dedup map, error swallow
//   src/AMR.DeliveryPlanning.Api/Infrastructure/Outbox/OutboxProcessorService.cs
//     ↑ pattern: continuous processing, backoff on error
//   src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Feeder/Services/Riot3ReconciliationService.cs
//     ↑ pattern: per-mode background service (will move to Transport.Amr in Phase 1)
//
// Critical conventions (from existing services):
//   1. Inject IServiceScopeFactory (NOT scoped services directly — BackgroundService is singleton)
//   2. Hot-reload config via IOptionsMonitor<T>, NOT IOptions<T> snapshot
//   3. Swallow exceptions in tick — never let one bad tick kill the service
//   4. OperationCanceledException on shutdown = clean exit, not error
//   5. Minimum delay 5 seconds (avoid runaway tight loops on misconfig)
//   6. Log Started + Stopped at INFO; per-tick at DEBUG or INFO with rate-limit
//   7. Idempotency: assume duplicate ticks possible (multi-instance deploy)
//
// DELETE THIS COMMENT BLOCK before committing.
// =============================================================================

using AMR.DeliveryPlanning.{Module}.Infrastructure.Data;
using AMR.DeliveryPlanning.SharedKernel.Diagnostics;       // remove if no metrics
using MassTransit;                                          // remove if no event publish
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AMR.DeliveryPlanning.{Module}.Application.BackgroundServices;

/// <summary>
/// {Purpose in 2-4 sentences:
///  - What it scans / does
///  - What problem it prevents (cite incident IDs if known)
///  - What the alarm signal is — if this service ever finds work to do, what
///    upstream bug does that imply?}
///
/// <para>Pattern lifted from {ReferenceService} — same poll loop, same
/// <see cref="IOptionsMonitor{TOptions}"/> hot reload, same defensive
/// exception handling.</para>
/// </summary>
public sealed class {Name}Service : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<{Name}Options> _options;
    private readonly WorkflowMetrics _metrics;             // optional — remove if no metrics
    private readonly ILogger<{Name}Service> _logger;

    // ─── In-memory state ──────────────────────────────────────────────────
    // Cleared on process restart — acceptable: a fresh pod is exactly the
    // case where re-firing is helpful. If you need durability, persist to
    // DB instead.

    /// <summary>Dedup so a wedged consumer doesn't get the same event re-fired
    /// every tick. Maps {target Id} → last-acted-at timestamp.</summary>
    private readonly ConcurrentDictionary<Guid, DateTime> _lastActedAt = new();

    public {Name}Service(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<{Name}Options> options,
        WorkflowMetrics metrics,
        ILogger<{Name}Service> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        _logger.LogInformation(
            "[{Name}] started (enabled={Enabled}, poll={Poll}s, threshold>{Threshold}s, capPerTick={Cap})",
            opts.Enabled, opts.PollIntervalSeconds, opts.StaleThresholdSeconds, opts.MaxActionsPerTick);

        while (!stoppingToken.IsCancellationRequested)
        {
            var current = _options.CurrentValue;

            // ─── Tick ─────────────────────────────────────────────────────
            try
            {
                if (current.Enabled)
                    await TickAsync(current, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;   // clean shutdown
            }
            catch (Exception ex)
            {
                // SWALLOW: one bad tick must NEVER kill the service.
                // Log + carry on. Metrics + alerting catch persistent failures.
                _logger.LogError(ex, "[{Name}] tick failed unexpectedly");
            }

            // ─── Wait for next tick ───────────────────────────────────────
            try
            {
                // Minimum 5s to avoid runaway loops on misconfig (PollIntervalSeconds = 0)
                var delay = TimeSpan.FromSeconds(Math.Max(5, current.PollIntervalSeconds));
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("[{Name}] stopped");
    }

    /// <summary>
    /// One pass of the work. Called on each interval if enabled.
    /// Resolve scoped services via the scope factory — DO NOT capture
    /// scoped services in constructor (this is a singleton).
    /// </summary>
    private async Task TickAsync({Name}Options opts, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<{YourDbContext}>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var now = DateTime.UtcNow;
        var threshold = now - TimeSpan.FromSeconds(opts.StaleThresholdSeconds);

        // ─── 1. Find candidates ───────────────────────────────────────────
        // Use AsNoTracking() — read-only scan
        // Use IgnoreQueryFilters() if vendor webhooks / system ops have no tenant context
        var candidates = await db.{YourTable}
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.SomeCondition && x.LastUpdatedAt < threshold)
            .Select(x => new { x.Id, x.SomeField })
            .Take(opts.MaxActionsPerTick * 2)   // overfetch — some may be deduped
            .ToListAsync(ct);

        // ─── 2. Update metrics (always, even if zero) ─────────────────────
        _metrics.SetSomeCounter(candidates.Count);

        if (candidates.Count == 0) return;

        // ─── 3. Dedup against in-memory state ─────────────────────────────
        var dedupCutoff = now - TimeSpan.FromSeconds(opts.DedupSeconds);
        var filtered = candidates
            .Where(c => !_lastActedAt.TryGetValue(c.Id, out var lastAt) || lastAt < dedupCutoff)
            .Take(opts.MaxActionsPerTick)
            .ToList();

        if (filtered.Count == 0) return;

        _logger.LogWarning(
            "[{Name}] found {Count} candidate(s) requiring action (cap {Cap})",
            filtered.Count, opts.MaxActionsPerTick);

        // ─── 4. Act on each candidate ─────────────────────────────────────
        foreach (var item in filtered)
        {
            try
            {
                // ── Your action here:
                // - Publish integration event:
                //   await publisher.Publish(new YourIntegrationEventV1(item.Id), ct);
                //
                // - Or send command via MediatR/ISender:
                //   var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                //   await sender.Send(new YourCommand(item.Id), ct);
                //
                // - Or call vendor API:
                //   var client = scope.ServiceProvider.GetRequiredService<IYourClient>();
                //   await client.DoThingAsync(item.Id, ct);

                _lastActedAt[item.Id] = now;
                _logger.LogInformation("[{Name}] acted on {Id}", item.Id);
            }
            catch (Exception ex)
            {
                // Per-item failure — log and continue with the rest
                _logger.LogWarning(ex, "[{Name}] failed to act on {Id}", item.Id);
            }
        }

        // ─── 5. Trim dedup map periodically (prevent unbounded growth) ────
        if (_lastActedAt.Count > 10_000)
        {
            foreach (var stale in _lastActedAt.Where(kv => kv.Value < dedupCutoff).Select(kv => kv.Key).ToList())
                _lastActedAt.TryRemove(stale, out _);
        }
    }
}


// =============================================================================
// COMPANION: Options class with hot-reload binding
// =============================================================================

namespace AMR.DeliveryPlanning.{Module}.Application.BackgroundServices;

public sealed class {Name}Options
{
    /// <summary>Master switch — allows ops to disable without redeploying.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Poll loop interval. Minimum 5 seconds enforced by service.</summary>
    public int PollIntervalSeconds { get; init; } = 60;

    /// <summary>How long an item must be untouched before considered stale.</summary>
    public int StaleThresholdSeconds { get; init; } = 300;

    /// <summary>Dedup window — same Id not re-acted upon within this period.</summary>
    public int DedupSeconds { get; init; } = 600;

    /// <summary>Maximum items acted upon per tick — protects downstream from flood.</summary>
    public int MaxActionsPerTick { get; init; } = 50;
}


// =============================================================================
// REGISTRATION (in your module's ServiceCollectionExtensions)
// =============================================================================

// public static IServiceCollection AddTransport{Mode}(this IServiceCollection services, IConfiguration config)
// {
//     services.Configure<{Name}Options>(config.GetSection("TransportModes:{Mode}:{Name}"));
//     services.AddHostedService<{Name}Service>();
//     return services;
// }


// =============================================================================
// CONFIG (appsettings.json)
// =============================================================================

// "TransportModes": {
//   "Manual": {
//     "Enabled": true,
//     "{Name}": {
//       "Enabled": true,
//       "PollIntervalSeconds": 60,
//       "StaleThresholdSeconds": 300,
//       "DedupSeconds": 600,
//       "MaxActionsPerTick": 50
//     }
//   }
// }
