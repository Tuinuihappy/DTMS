using System.Diagnostics;
using System.Text.Json;
using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.Dispatch.Infrastructure.Data;
using DTMS.Fleet.Infrastructure.Data;
using DTMS.Planning.Infrastructure.Data;
using DTMS.SharedKernel.Diagnostics;
using DTMS.SharedKernel.Domain;
using DTMS.SharedKernel.Outbox;
using DTMS.Transport.Amr.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DTMS.Api.Infrastructure.Outbox;

public class OutboxProcessorService : BackgroundService, IOutboxProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<OutboxOptions> _options;
    private readonly WorkflowMetrics _metrics;
    private readonly IOutboxWakeSignal _wakeSignal;
    private readonly ILogger<OutboxProcessorService> _logger;

    // Phase O3 — adaptive polling. Empty ticks double the delay from
    // AdaptivePollBaseMs to AdaptivePollMaxMs. Any of (fetched > 0 |
    // NOTIFY wake | manual replay) resets to 0.
    private int _consecutiveEmptyTicks;

    public OutboxProcessorService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<OutboxOptions> options,
        WorkflowMetrics metrics,
        IOutboxWakeSignal wakeSignal,
        ILogger<OutboxProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _metrics = metrics;
        _wakeSignal = wakeSignal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        _logger.LogInformation(
            "OutboxProcessorService started (UseSkipLocked={UseSkipLocked}, BatchSize={BatchSize}, PublishConcurrency={PublishConcurrency}, PollIntervalSeconds={PollIntervalSeconds}, PerMessageTimeoutSeconds={PerMessageTimeoutSeconds}, UseListenNotify={UseListenNotify}, AdaptivePollBaseMs={AdaptivePollBaseMs}, AdaptivePollMaxMs={AdaptivePollMaxMs})",
            opts.UseSkipLocked, opts.BatchSize, opts.PublishConcurrency, opts.PollIntervalSeconds, opts.PerMessageTimeoutSeconds, opts.UseListenNotify, opts.AdaptivePollBaseMs, opts.AdaptivePollMaxMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            int processed = 0;
            try
            {
                processed = await ProcessUnpublishedEventsInternalAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            // Phase O3 — adaptive delay. Non-empty tick resets the backoff
            // (there's flow, drain fast). Empty tick doubles until it hits
            // AdaptivePollMaxMs (idle steady state).
            if (processed > 0)
                _consecutiveEmptyTicks = 0;
            else
                _consecutiveEmptyTicks++;

            var currentOpts = _options.CurrentValue;
            var delayMs = CalculateAdaptiveDelayMs(_consecutiveEmptyTicks,
                currentOpts.AdaptivePollBaseMs, currentOpts.AdaptivePollMaxMs);

            // Phase O2 — wait for the NEXT of two events: (a) adaptive poll
            // timer, or (b) a NOTIFY signal delivered by OutboxListenerService.
            // First-to-fire wins; the other is cancelled so we don't leak
            // Task instances between ticks. Poll interval remains as safety
            // net — a missed notification (listener disconnect, unlucky
            // commit ordering) is picked up ≤delayMs later without any
            // correctness gap.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var delayTask = Task.Delay(TimeSpan.FromMilliseconds(delayMs), linked.Token);
            var wakeTask = _wakeSignal.WaitAsync(linked.Token);
            try
            {
                var completed = await Task.WhenAny(delayTask, wakeTask);
                if (completed == wakeTask)
                {
                    // NOTIFY arrived — reset backoff even if the wake races
                    // with an empty processing tick just before it.
                    _consecutiveEmptyTicks = 0;
                }
            }
            catch (OperationCanceledException) { break; }
            finally
            {
                linked.Cancel();
            }
        }
    }

    /// <summary>
    /// Phase O3 — exponential backoff clamped to the configured max.
    /// Zero empty ticks = base delay; capped at AdaptivePollMaxMs. Public
    /// static so a unit test can exercise it without spinning up the
    /// hosted service.
    /// </summary>
    public static int CalculateAdaptiveDelayMs(int consecutiveEmptyTicks, int baseMs, int maxMs)
    {
        var safeBase = Math.Max(1, baseMs);
        var safeMax = Math.Max(safeBase, maxMs);
        if (consecutiveEmptyTicks <= 0) return safeBase;
        // Clamp exponent to avoid double overflow for absurd values.
        var exponent = Math.Min(consecutiveEmptyTicks, 20);
        var delay = safeBase * Math.Pow(2, exponent);
        return (int)Math.Min(delay, safeMax);
    }

    public async Task ProcessUnpublishedEventsAsync(CancellationToken cancellationToken = default)
    {
        await ProcessUnpublishedEventsInternalAsync(cancellationToken);
    }

    /// <summary>
    /// Phase O3 internal — same as the public method but returns the total
    /// number of messages processed this tick. Used by ExecuteAsync to
    /// drive the adaptive-poll counter.
    /// </summary>
    private async Task<int> ProcessUnpublishedEventsInternalAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot per-tick so a config edit landing mid-tick doesn't mix paths
        // across modules. The IOptionsMonitor wrapper guarantees a hot-reload
        // takes effect on the NEXT tick after the file change.
        var opts = _options.CurrentValue;

        using var scope = _scopeFactory.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var dlq = scope.ServiceProvider.GetRequiredService<DTMS.SharedKernel.Outbox.IDeadLetterStore>();

        // Phase A Step A4 — parallel per-module loop. Each module's tick is
        // ~500-800ms with full SKIP LOCKED tx + parallel publish + SaveChanges +
        // count. Sequential await across 6 modules made the wall-clock cycle
        // ~4.2s even though PollIntervalSeconds=1, capping observed drain at
        // ~143/s/module. Task.WhenAll lets all 6 tick concurrently — cycle
        // wall-clock = max(per-module), not sum, ~5x drain throughput.
        //
        // Safety: each DbContext resolution from `scope` returns its own
        // instance — they're never shared across threads. IPublishEndpoint
        // (MassTransit) is thread-safe per its guarantees. One module's
        // fault is caught + logged per-module so the other 5 keep draining
        // (an Exception escaping the body would cancel the rest of WhenAll
        // and surface as a noisy "Error processing outbox messages" log
        // every poll until the offending module recovers).
        //
        // Outbox ownership (the real partition of work — by what a row MEANS,
        // not which schema it happens to live in):
        //   • MultiPartitionOutboxProcessor owns the PARTITIONED rows in the
        //     central `outbox` schema (PartitionKey != null) — HTTP callbacks
        //     fanned to per-system workers.
        //   • THIS service owns every NULL-partition row = an integration event
        //     going out via MassTransit. That is the 5 module tables (which
        //     only ever hold null-partition rows — PartitionKey isn't even
        //     mapped there) PLUS the null-partition rows in the central `outbox`
        //     schema (e.g. SourceCallbackOutcome written by MultiPartition).
        // Before this pass existed, those central null-partition rows were
        // drained by nobody — each processor's comment assumed the other owned
        // them — and stranded silently. The central pass MUST filter
        // PartitionKey IS NULL so it never races MultiPartition for a
        // partitioned row (double-delivery); see FetchCentralBatch* below.
        var modules = new (DbContext db, string source)[]
        {
            (scope.ServiceProvider.GetRequiredService<DeliveryOrderDbContext>(), DeliveryOrderDbContext.Schema),
            (scope.ServiceProvider.GetRequiredService<PlanningDbContext>(), PlanningDbContext.Schema),
            (scope.ServiceProvider.GetRequiredService<DispatchDbContext>(), DispatchDbContext.Schema),
            (scope.ServiceProvider.GetRequiredService<FleetDbContext>(), FleetDbContext.Schema),
            (scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>(), VendorAdapterDbContext.Schema),
        };

        var perModuleResults = await Task.WhenAll(modules.Select(async m =>
        {
            try
            {
                return await ProcessModuleAsync(m.db, publisher, dlq, m.source, opts, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Outbox module {Source} tick failed; other modules unaffected", m.source);
                return (Pending: 0L, Processed: 0);
            }
        }));

        // Central `outbox` schema — null-partition rows only. Runs on the same
        // scope's OutboxDbContext; isolated in its own try/catch so a central
        // fault doesn't cancel the module results (and vice-versa).
        var centralOutbox = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        (long Pending, int Processed) centralResult;
        try
        {
            centralResult = await ProcessCentralOutboxAsync(centralOutbox, publisher, dlq, opts, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Central outbox tick failed; module drains unaffected");
            centralResult = (0L, 0);
        }

        // Aggregate pending across ALL schemas the processor owns — the 5
        // modules and the central null-partition slice — so the
        // dtms_workflow_outbox_pending gauge reflects the true backlog. (It
        // previously summed modules only, so a strand in the central schema
        // — exactly the failure this pass fixes — was invisible to the gauge.)
        _metrics.SetOutboxPending(perModuleResults.Sum(r => r.Pending) + centralResult.Pending);

        // Oldest un-processed row age. Unlike outbox_age_seconds (recorded only
        // on a SUCCESSFUL publish, so it can never see a row that nobody
        // drains), this reads the backlog directly — the one signal that would
        // have caught the week-long strand.
        _metrics.SetOutboxOldestPendingAgeSeconds(await MaxPendingAgeSecondsAsync(centralOutbox, modules, cancellationToken));

        return perModuleResults.Sum(r => r.Processed) + centralResult.Processed;
    }

    // Central `outbox` schema drain — the ONLY fetch that filters on
    // PartitionKey (the column exists here; the 5 module schemas Ignore it).
    // PartitionKey IS NULL is the double-delivery guard: partitioned rows
    // belong to MultiPartitionOutboxProcessor and must not be published here.
    internal async Task<(long Pending, int Processed)> ProcessCentralOutboxAsync(
        OutboxDbContext db,
        IPublishEndpoint publisher,
        DTMS.SharedKernel.Outbox.IDeadLetterStore dlq,
        OutboxOptions opts,
        CancellationToken cancellationToken)
    {
        const string source = OutboxDbContext.Schema;
        if (!opts.UseSkipLocked)
        {
            var messages = await FetchCentralBatchAsync(db, opts.BatchSize, cancellationToken);
            if (messages.Count == 0)
                return (await CountCentralPendingAsync(db, cancellationToken), 0);

            await PublishBatchAsync(messages, publisher, dlq, db, source, opts, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return (await CountCentralPendingAsync(db, cancellationToken), messages.Count);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            var messages = await FetchCentralBatchSkipLockedAsync(db, opts.BatchSize, cancellationToken);
            if (messages.Count == 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return (await CountCentralPendingAsync(db, cancellationToken), 0);
            }

            _logger.LogDebug("Processing {Count} central outbox messages (SKIP LOCKED)", messages.Count);

            await PublishBatchAsync(messages, publisher, dlq, db, source, opts, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return (await CountCentralPendingAsync(db, cancellationToken), messages.Count);
        });
    }

    private Task<(long Pending, int Processed)> ProcessModuleAsync(
        DbContext db,
        IPublishEndpoint publisher,
        DTMS.SharedKernel.Outbox.IDeadLetterStore dlq,
        string source,
        OutboxOptions opts,
        CancellationToken cancellationToken)
    {
        return opts.UseSkipLocked
            ? ProcessModuleSkipLockedAsync(db, publisher, dlq, source, opts, cancellationToken)
            : ProcessModuleLegacyAsync(db, publisher, dlq, source, opts, cancellationToken);
    }

    private async Task<(long Pending, int Processed)> ProcessModuleLegacyAsync(
        DbContext db,
        IPublishEndpoint publisher,
        DTMS.SharedKernel.Outbox.IDeadLetterStore dlq,
        string source,
        OutboxOptions opts,
        CancellationToken cancellationToken)
    {
        var messages = await FetchBatchAsync(db, opts.BatchSize, cancellationToken);

        if (messages.Count == 0)
        {
            return (await CountPendingAsync(db, cancellationToken), 0);
        }

        _logger.LogDebug("Processing {Count} outbox messages from {Source}", messages.Count, source);

        await PublishBatchAsync(messages, publisher, dlq, db, source, opts, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return (await CountPendingAsync(db, cancellationToken), messages.Count);
    }

    // SKIP LOCKED path — the FOR UPDATE row locks are held inside an explicit
    // transaction that wraps fetch + publish + save. While the tx is open, a
    // second outbox worker (Phase D — multiple replicas) sees the same rows as
    // locked and SKIPs them rather than fighting for them. Locks release on
    // commit/rollback, never leaking past the tick.
    private async Task<(long Pending, int Processed)> ProcessModuleSkipLockedAsync(
        DbContext db,
        IPublishEndpoint publisher,
        DTMS.SharedKernel.Outbox.IDeadLetterStore dlq,
        string source,
        OutboxOptions opts,
        CancellationToken cancellationToken)
    {
        // CreateExecutionStrategy is required for explicit transactions when
        // the DbContext has EnableRetryOnFailure configured (see
        // ModuleServiceRegistration.ConfigureNpgsql). Without this wrap, EF
        // Core throws "The configured execution strategy does not support
        // user-initiated transactions" on first use. Replay-on-retry is
        // safe because consumers are already idempotent — outbox is
        // at-least-once by design.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            var messages = await FetchBatchSkipLockedAsync(db, source, opts.BatchSize, cancellationToken);

            if (messages.Count == 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return (await CountPendingAsync(db, cancellationToken), 0);
            }

            _logger.LogDebug("Processing {Count} outbox messages from {Source} (SKIP LOCKED)",
                messages.Count, source);

            await PublishBatchAsync(messages, publisher, dlq, db, source, opts, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return (await CountPendingAsync(db, cancellationToken), messages.Count);
        });
    }

    // Pulled out of ProcessModuleAsync so Step A3 can branch on Outbox:UseSkipLocked
    // — the SKIP LOCKED path is a sibling that returns the same shape but uses
    // raw SQL with FOR UPDATE SKIP LOCKED. Keeps the publish loop reusable.
    private static async Task<List<OutboxMessage>> FetchBatchAsync(
        DbContext db, int batchSize, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null
                        && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= now))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    // Raw SQL path. Postgres FOR UPDATE SKIP LOCKED is not expressible via EF
    // LINQ — only raw SQL. The `schema` parameter comes from each DbContext's
    // compile-time Schema constant (not user input), so direct interpolation
    // into the identifier position is safe; the LIMIT goes through the {0}
    // parameter slot so the query-plan cache stays hot across batch-size
    // changes.
    private static async Task<List<OutboxMessage>> FetchBatchSkipLockedAsync(
        DbContext db, string schema, int batchSize, CancellationToken cancellationToken)
    {
        var sql = $@"SELECT * FROM {schema}.""OutboxMessages""
                     WHERE ""ProcessedOnUtc"" IS NULL
                       AND (""NextRetryAtUtc"" IS NULL OR ""NextRetryAtUtc"" <= NOW())
                     ORDER BY ""OccurredOnUtc""
                     LIMIT {{0}}
                     FOR UPDATE SKIP LOCKED";

        return await db.Set<OutboxMessage>()
            .FromSqlRaw(sql, batchSize)
            .ToListAsync(cancellationToken);
    }

    private static Task<int> CountPendingAsync(DbContext db, CancellationToken cancellationToken) =>
        db.Set<OutboxMessage>().CountAsync(m => m.ProcessedOnUtc == null, cancellationToken);

    // Central-schema variants — identical to the module fetches but with the
    // `PartitionKey IS NULL` guard. Kept separate (not a predicate param on the
    // shared helpers) because the 5 module contexts Ignore PartitionKey, so the
    // LINQ path can't translate it and the raw-SQL path hits 42703 there.
    internal static async Task<List<OutboxMessage>> FetchCentralBatchAsync(
        OutboxDbContext db, int batchSize, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return await db.OutboxMessages
            .Where(m => m.PartitionKey == null
                        && m.ProcessedOnUtc == null
                        && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= now))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    private static async Task<List<OutboxMessage>> FetchCentralBatchSkipLockedAsync(
        OutboxDbContext db, int batchSize, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT * FROM outbox.""OutboxMessages""
                     WHERE ""PartitionKey"" IS NULL
                       AND ""ProcessedOnUtc"" IS NULL
                       AND (""NextRetryAtUtc"" IS NULL OR ""NextRetryAtUtc"" <= NOW())
                     ORDER BY ""OccurredOnUtc""
                     LIMIT {0}
                     FOR UPDATE SKIP LOCKED";

        return await db.OutboxMessages
            .FromSqlRaw(sql, batchSize)
            .ToListAsync(cancellationToken);
    }

    private static Task<int> CountCentralPendingAsync(OutboxDbContext db, CancellationToken cancellationToken) =>
        db.OutboxMessages.CountAsync(m => m.PartitionKey == null && m.ProcessedOnUtc == null, cancellationToken);

    // Oldest pending row across every schema this service owns, in seconds.
    // Reads the backlog directly (MIN OccurredOnUtc of unprocessed rows) so a
    // stranded row is visible even though it is never published — the gap that
    // let SourceCallbackOutcome rows sit unnoticed for a week.
    private static async Task<double> MaxPendingAgeSecondsAsync(
        OutboxDbContext central, (DbContext db, string source)[] modules, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        DateTime? oldest = await central.OutboxMessages
            .Where(m => m.PartitionKey == null && m.ProcessedOnUtc == null)
            .MinAsync(m => (DateTime?)m.OccurredOnUtc, cancellationToken);

        foreach (var (db, _) in modules)
        {
            var moduleOldest = await db.Set<OutboxMessage>()
                .Where(m => m.ProcessedOnUtc == null)
                .MinAsync(m => (DateTime?)m.OccurredOnUtc, cancellationToken);
            if (moduleOldest is { } mo && (oldest is null || mo < oldest))
                oldest = mo;
        }

        return oldest is { } o ? Math.Max(0, (now - o).TotalSeconds) : 0;
    }

    // Two-phase publish: parallel publish + sequential mutate. The DbContext
    // is NOT thread-safe — MarkAsProcessed/MarkAsFailed mutate change-tracked
    // entities, so those calls must be single-threaded. IPublishEndpoint
    // (MassTransit) IS thread-safe, so the publish itself runs in parallel
    // bounded by opts.PublishConcurrency. With PublishConcurrency=1 the timing
    // is identical to the pre-flag sequential foreach.
    private async Task PublishBatchAsync(
        List<OutboxMessage> messages,
        IPublishEndpoint publisher,
        DTMS.SharedKernel.Outbox.IDeadLetterStore dlq,
        DbContext db,
        string source,
        OutboxOptions opts,
        CancellationToken cancellationToken)
    {
        var results = new (DateTime? PublishedAt, Exception? Error)[messages.Count];
        var publishTimeout = TimeSpan.FromSeconds(Math.Max(1, opts.PerMessageTimeoutSeconds));

        // Phase 1 — publish in parallel. Each task writes its result into the
        // pre-allocated results[] slot at the same index — no shared mutable
        // collection, no lock needed. PublishConcurrency clamped to >=1.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, messages.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, opts.PublishConcurrency),
                CancellationToken = cancellationToken,
            },
            async (i, ct) =>
            {
                var message = messages[i];

                // Phase O4 — restore the trace context captured at outbox-
                // write time so the publish + consumer + projection spans
                // chain under the original request's root. When TraceParent
                // is missing (pre-O4 rows, background-write paths) the
                // Activity starts as a fresh root — still a valid trace,
                // just detached from the origin. Activity kind = Producer
                // per OTel semantic conventions for messaging producers.
                ActivityContext parentContext = default;
                if (!string.IsNullOrEmpty(message.TraceParent))
                {
                    ActivityContext.TryParse(message.TraceParent, null, out parentContext);
                }
                using var activity = DTMS.SharedKernel.Diagnostics.OutboxActivitySource.Source
                    .StartActivity("outbox.publish", ActivityKind.Producer, parentContext);
                activity?.SetTag("outbox.message_id", message.Id);
                activity?.SetTag("outbox.source", source);
                activity?.SetTag("outbox.type", message.Type);

                try
                {
                    var type = Type.GetType(message.Type);
                    if (type == null)
                    {
                        var typeError = new InvalidOperationException($"Type not found: {message.Type}");
                        activity?.SetStatus(ActivityStatusCode.Error, typeError.Message);
                        results[i] = (null, typeError);
                        return;
                    }

                    var payload = JsonSerializer.Deserialize(message.Content, type);
                    if (payload is IIntegrationEvent integrationEvent)
                    {
                        // Per-publish timeout: fail fast when MassTransit bus is unavailable
                        // (e.g., RabbitMQ not reachable) rather than blocking indefinitely.
                        using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        publishCts.CancelAfter(publishTimeout);
                        await publisher.Publish(integrationEvent, type, publishCts.Token);
                    }

                    var publishedAt = DateTime.UtcNow;
                    // T1.6 — record (publish-time - occurrence-time). OpenTelemetry
                    // histograms are thread-safe so calling from the parallel block
                    // is fine.
                    _metrics.RecordOutboxAge((publishedAt - message.OccurredOnUtc).TotalSeconds);
                    results[i] = (publishedAt, null);
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.AddException(ex);
                    results[i] = (null, ex);
                }
            });

        // Phase 2 — mutate entities sequentially on the calling thread. The
        // DbContext's change tracker stays consistent because there's only one
        // writer here.
        for (var i = 0; i < messages.Count; i++)
        {
            var (publishedAt, error) = results[i];
            if (error == null && publishedAt.HasValue)
            {
                messages[i].MarkAsProcessed(publishedAt.Value);
            }
            else
            {
                await HandleFailureAsync(messages[i], dlq, db, source, error?.Message ?? "Unknown publish failure", error, cancellationToken);
            }
        }
    }

    private async Task HandleFailureAsync(
        OutboxMessage message,
        DTMS.SharedKernel.Outbox.IDeadLetterStore dlq,
        DbContext db,
        string source,
        string error,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        message.MarkAsFailed(now, error);

        if (!message.HasReachedMaxRetries)
        {
            _logger.LogWarning(exception, "Outbox message {Id} from {Source} failed (attempt {Count}/{Max}); next retry at {NextRetry:o}: {Error}",
                message.Id, source, message.RetryCount, OutboxRetryPolicy.MaxRetries, message.NextRetryAtUtc, error);
            return;
        }

        _logger.LogError(exception,
            "Outbox message {Id} from {Source} permanently failed after {Max} attempts: {Error}",
            message.Id, source, OutboxRetryPolicy.MaxRetries, error);

        // Phase O3 — move to central DLQ + physically remove from module
        // table. Both wrapped in try/catch: on failure, the row stays in
        // its terminal-in-place state (ProcessedOnUtc set, blocked from
        // re-publish) and ops sees the discrepancy via metric spread —
        // the periodic DlqSweeperService (if wired) can re-attempt.
        // OriginalOutboxId has a UNIQUE constraint so MoveAsync is
        // idempotent — a re-attempt on partial-success (insert OK, delete
        // failed) is a no-op.
        try
        {
            var firstFailed = message.OccurredOnUtc; // approximation — first-fail time isn't tracked on the entity
            await dlq.MoveAsync(message, source, firstFailed, now, cancellationToken);
            db.Set<OutboxMessage>().Remove(message);
        }
        catch (Exception moveEx)
        {
            _logger.LogError(moveEx,
                "DLQ move failed for {Id} from {Source} — row stays terminal in module table; will retry on next tick or via sweeper",
                message.Id, source);
        }
    }
}
