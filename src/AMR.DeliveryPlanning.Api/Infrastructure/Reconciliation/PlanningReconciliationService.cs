using System.Collections.Concurrent;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ReplanStuckOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using AMR.DeliveryPlanning.SharedKernel.Diagnostics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.Api.Infrastructure.Reconciliation;

/// <summary>
/// T1.4 — safety net for the Planning workflow. Scans for orders that are
/// stuck at <see cref="OrderStatus.Planned"/> with no associated Trip rows
/// (the OD-0374 / OD-0375 incident shape) and re-publishes
/// <see cref="DeliveryOrderConfirmedIntegrationEventV1"/> so the Planning
/// consumer takes another swing. Relies on the idempotency guards added in
/// T1.5 (CreateJobAnchor + MarkJobDispatched) to be safe under replay.
///
/// <para>Pattern lifted from
/// <c>Riot3ReconciliationService</c> — same poll loop, same
/// <see cref="IOptionsMonitor{TOptions}"/> hot reload, same defensive
/// exception handling. The watchdog is the system's last line of defence
/// against the broker losing an ack mid-consume; if it ever logs a non-zero
/// stuck count investigate the consumer, don't ignore the alert.</para>
/// </summary>
public sealed class PlanningReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<PlanningWatchdogOptions> _options;
    private readonly WorkflowMetrics _metrics;
    private readonly ILogger<PlanningReconciliationService> _logger;

    // In-memory dedup so a wedged consumer doesn't get the same event re-fired
    // every tick. Cleared on process restart — acceptable: a fresh start with
    // a different pod is exactly the case where re-firing is helpful.
    private readonly ConcurrentDictionary<Guid, DateTime> _lastReplayedAt = new();

    public PlanningReconciliationService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<PlanningWatchdogOptions> options,
        WorkflowMetrics metrics,
        ILogger<PlanningReconciliationService> logger)
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
            "[PlanningWatchdog] started (enabled={Enabled}, poll={Poll}s, stale>{Stale}s, dedup={Dedup}s, capPerTick={Cap})",
            opts.Enabled, opts.PollIntervalSeconds, opts.StaleThresholdSeconds,
            opts.ReplayDedupSeconds, opts.MaxReplaysPerTick);

        while (!stoppingToken.IsCancellationRequested)
        {
            var current = _options.CurrentValue;
            try
            {
                if (current.Enabled)
                    await TickAsync(current, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PlanningWatchdog] tick failed unexpectedly");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, current.PollIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("[PlanningWatchdog] stopped");
    }

    private async Task TickAsync(PlanningWatchdogOptions opts, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var deliveryDb = scope.ServiceProvider.GetRequiredService<DeliveryOrderDbContext>();
        var dispatchDb = scope.ServiceProvider.GetRequiredService<DispatchDbContext>();
        var planningDb = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var now = DateTime.UtcNow;
        var staleBefore = now - TimeSpan.FromSeconds(opts.StaleThresholdSeconds);

        // Candidate set: orders at Planned that haven't been touched since the
        // stale cutoff. UpdatedDate is bumped by every state transition + the
        // owning aggregate's SaveChangesInterceptor, so a sticky Planned row
        // means nothing has moved the order forward.
        var candidates = await deliveryDb.DeliveryOrders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Planned
                        && (o.UpdatedDate ?? o.CreatedDate) < staleBefore)
            .Select(o => new { o.Id, o.OrderRef })
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            _metrics.SetOrdersStuckPlanned(0);
            return;
        }

        // Filter out orders that already have a Trip in dispatch.Trips — those
        // are progressing normally, the Planned status just hasn't advanced
        // yet (e.g. MarkOrderDispatched in-flight, MassTransit retry pending).
        var candidateIds = candidates.Select(c => c.Id).ToList();
        var idsWithTrips = await dispatchDb.Trips
            .AsNoTracking()
            .Where(t => candidateIds.Contains(t.DeliveryOrderId))
            .Select(t => t.DeliveryOrderId)
            .Distinct()
            .ToListAsync(ct);

        // T1.8 — also filter orders whose Jobs already carry a VendorOrderKey.
        // Vendor accepted the upperKey on a prior attempt — replaying would
        // re-send the same upperKey and RIOT3 would reject with E110007
        // "upper-level unique key duplicate", spinning the watchdog forever
        // (the OD-0381 incident shape). Reconciliation, not replay, is the
        // right tool for these.
        var idsVendorAccepted = await planningDb.Jobs
            .AsNoTracking()
            .Where(j => candidateIds.Contains(j.DeliveryOrderId) && j.VendorOrderKey != null)
            .Select(j => j.DeliveryOrderId)
            .Distinct()
            .ToListAsync(ct);

        var trueStuck = candidates
            .Where(c => !idsWithTrips.Contains(c.Id) && !idsVendorAccepted.Contains(c.Id))
            .ToList();
        _metrics.SetOrdersStuckPlanned(trueStuck.Count);

        if (idsVendorAccepted.Count > 0)
            _logger.LogInformation(
                "[PlanningWatchdog] skipped {Count} candidate(s) whose vendor already accepted upperKey — reconciliation required",
                idsVendorAccepted.Count);

        if (trueStuck.Count == 0) return;

        _logger.LogWarning(
            "[PlanningWatchdog] found {Count} stuck Planned order(s) with no Trip — replaying ConfirmedEvent (cap {Cap})",
            trueStuck.Count, opts.MaxReplaysPerTick);

        var dedupCutoff = now - TimeSpan.FromSeconds(opts.ReplayDedupSeconds);
        var replayed = 0;

        foreach (var stuck in trueStuck)
        {
            if (ct.IsCancellationRequested) break;
            if (replayed >= opts.MaxReplaysPerTick)
            {
                _logger.LogInformation(
                    "[PlanningWatchdog] hit per-tick cap {Cap} — remaining {Left} order(s) deferred to next tick",
                    opts.MaxReplaysPerTick, trueStuck.Count - replayed);
                break;
            }

            // In-memory dedup: don't re-fire if we already did within the
            // ReplayDedup window (consumer may simply be slow this round).
            if (_lastReplayedAt.TryGetValue(stuck.Id, out var lastAt) && lastAt > dedupCutoff)
                continue;

            try
            {
                // Same command as the admin /replan endpoint — single replay
                // implementation for manual + automatic recovery paths. The
                // RequireStuckPlanned flag asks the handler to skip if the
                // order moved between our scan and the send (e.g. another pod
                // beat us to it).
                var result = await sender.Send(new ReplanStuckOrderCommand(
                    OrderId: stuck.Id,
                    TriggeredBy: "PlanningWatchdog",
                    Reason: $"auto-replay: stuck Planned with no Trip > {opts.StaleThresholdSeconds}s",
                    RequireStuckPlanned: true), ct);

                if (result.IsSuccess)
                {
                    _lastReplayedAt[stuck.Id] = now;
                    _metrics.RecordWatchdogReplay("stuck_planned_no_trip");
                    replayed++;
                }
                else
                {
                    _logger.LogWarning(
                        "[PlanningWatchdog] skipped replay for {OrderRef} ({OrderId}): {Reason}",
                        stuck.OrderRef, stuck.Id, result.Error);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[PlanningWatchdog] failed to replay order {OrderRef} ({OrderId})",
                    stuck.OrderRef, stuck.Id);
            }
        }

        // GC the dedup table so it doesn't grow forever — drop entries past
        // the dedup window since they're no longer relevant.
        foreach (var kv in _lastReplayedAt)
        {
            if (kv.Value < dedupCutoff)
                _lastReplayedAt.TryRemove(kv.Key, out _);
        }
    }
}
