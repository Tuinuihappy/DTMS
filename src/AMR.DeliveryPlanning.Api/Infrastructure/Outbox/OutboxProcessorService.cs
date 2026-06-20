using System.Text.Json;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using AMR.DeliveryPlanning.SharedKernel.Diagnostics;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Outbox;
using AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.Api.Infrastructure.Outbox;

public class OutboxProcessorService : BackgroundService, IOutboxProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<OutboxOptions> _options;
    private readonly WorkflowMetrics _metrics;
    private readonly ILogger<OutboxProcessorService> _logger;

    public OutboxProcessorService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<OutboxOptions> options,
        WorkflowMetrics metrics,
        ILogger<OutboxProcessorService> logger)
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
            "OutboxProcessorService started (UseSkipLocked={UseSkipLocked}, BatchSize={BatchSize}, PublishConcurrency={PublishConcurrency}, PollIntervalSeconds={PollIntervalSeconds}, PerMessageTimeoutSeconds={PerMessageTimeoutSeconds})",
            opts.UseSkipLocked, opts.BatchSize, opts.PublishConcurrency, opts.PollIntervalSeconds, opts.PerMessageTimeoutSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessUnpublishedEventsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            // Hot-reload friendly — read PollIntervalSeconds per iteration so a
            // config edit takes effect on the NEXT sleep. Clamped to >=1s so a
            // misconfigured 0 doesn't become a busy loop.
            var pollSeconds = Math.Max(1, _options.CurrentValue.PollIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
        }
    }

    public async Task ProcessUnpublishedEventsAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot per-tick so a config edit landing mid-tick doesn't mix paths
        // across modules. The IOptionsMonitor wrapper guarantees a hot-reload
        // takes effect on the NEXT tick after the file change.
        var opts = _options.CurrentValue;

        using var scope = _scopeFactory.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        // T1.6 — aggregate pending count across all schemas per poll so the
        // dtms_workflow_outbox_pending gauge reflects current backlog. If this
        // climbs > 500 sustained the outbox is falling behind and ops should
        // page (see plan section 5).
        long totalPending = 0;
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<OutboxDbContext>(), publisher, "outbox", opts, cancellationToken);
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<DeliveryOrderDbContext>(), publisher, DeliveryOrderDbContext.Schema, opts, cancellationToken);
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<PlanningDbContext>(), publisher, PlanningDbContext.Schema, opts, cancellationToken);
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<DispatchDbContext>(), publisher, DispatchDbContext.Schema, opts, cancellationToken);
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<FleetDbContext>(), publisher, FleetDbContext.Schema, opts, cancellationToken);
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>(), publisher, VendorAdapterDbContext.Schema, opts, cancellationToken);

        _metrics.SetOutboxPending(totalPending);
    }

    private Task<long> ProcessModuleAsync(
        DbContext db,
        IPublishEndpoint publisher,
        string source,
        OutboxOptions opts,
        CancellationToken cancellationToken)
    {
        return opts.UseSkipLocked
            ? ProcessModuleSkipLockedAsync(db, publisher, source, opts, cancellationToken)
            : ProcessModuleLegacyAsync(db, publisher, source, opts, cancellationToken);
    }

    private async Task<long> ProcessModuleLegacyAsync(
        DbContext db,
        IPublishEndpoint publisher,
        string source,
        OutboxOptions opts,
        CancellationToken cancellationToken)
    {
        var messages = await FetchBatchAsync(db, opts.BatchSize, cancellationToken);

        if (messages.Count == 0)
        {
            return await CountPendingAsync(db, cancellationToken);
        }

        _logger.LogDebug("Processing {Count} outbox messages from {Source}", messages.Count, source);

        await PublishBatchAsync(messages, publisher, source, opts, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return await CountPendingAsync(db, cancellationToken);
    }

    // SKIP LOCKED path — the FOR UPDATE row locks are held inside an explicit
    // transaction that wraps fetch + publish + save. While the tx is open, a
    // second outbox worker (Phase D — multiple replicas) sees the same rows as
    // locked and SKIPs them rather than fighting for them. Locks release on
    // commit/rollback, never leaking past the tick.
    private async Task<long> ProcessModuleSkipLockedAsync(
        DbContext db,
        IPublishEndpoint publisher,
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
                return await CountPendingAsync(db, cancellationToken);
            }

            _logger.LogDebug("Processing {Count} outbox messages from {Source} (SKIP LOCKED)",
                messages.Count, source);

            await PublishBatchAsync(messages, publisher, source, opts, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return await CountPendingAsync(db, cancellationToken);
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

    // Two-phase publish: parallel publish + sequential mutate. The DbContext
    // is NOT thread-safe — MarkAsProcessed/MarkAsFailed mutate change-tracked
    // entities, so those calls must be single-threaded. IPublishEndpoint
    // (MassTransit) IS thread-safe, so the publish itself runs in parallel
    // bounded by opts.PublishConcurrency. With PublishConcurrency=1 the timing
    // is identical to the pre-flag sequential foreach.
    private async Task PublishBatchAsync(
        List<OutboxMessage> messages,
        IPublishEndpoint publisher,
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
                try
                {
                    var type = Type.GetType(message.Type);
                    if (type == null)
                    {
                        results[i] = (null, new InvalidOperationException($"Type not found: {message.Type}"));
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
                HandleFailure(messages[i], source, error?.Message ?? "Unknown publish failure", error);
            }
        }
    }

    private void HandleFailure(OutboxMessage message, string source, string error, Exception? exception)
    {
        message.MarkAsFailed(DateTime.UtcNow, error);

        if (message.HasReachedMaxRetries)
        {
            _logger.LogError(exception, "Outbox message {Id} from {Source} permanently failed after {Max} attempts: {Error}",
                message.Id, source, OutboxRetryPolicy.MaxRetries, error);
        }
        else
        {
            _logger.LogWarning(exception, "Outbox message {Id} from {Source} failed (attempt {Count}/{Max}); next retry at {NextRetry:o}: {Error}",
                message.Id, source, message.RetryCount, OutboxRetryPolicy.MaxRetries, message.NextRetryAtUtc, error);
        }
    }
}
