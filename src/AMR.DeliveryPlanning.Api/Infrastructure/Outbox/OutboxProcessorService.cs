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
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

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
            "OutboxProcessorService started (UseSkipLocked={UseSkipLocked}, BatchSize={BatchSize})",
            opts.UseSkipLocked, opts.BatchSize);

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

            await Task.Delay(PollingInterval, stoppingToken);
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
            ? ProcessModuleSkipLockedAsync(db, publisher, source, opts.BatchSize, cancellationToken)
            : ProcessModuleLegacyAsync(db, publisher, source, opts.BatchSize, cancellationToken);
    }

    private async Task<long> ProcessModuleLegacyAsync(
        DbContext db,
        IPublishEndpoint publisher,
        string source,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var messages = await FetchBatchAsync(db, batchSize, cancellationToken);

        if (messages.Count == 0)
        {
            return await CountPendingAsync(db, cancellationToken);
        }

        _logger.LogDebug("Processing {Count} outbox messages from {Source}", messages.Count, source);

        await PublishBatchAsync(messages, publisher, source, cancellationToken);
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
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var messages = await FetchBatchSkipLockedAsync(db, source, batchSize, cancellationToken);

        if (messages.Count == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return await CountPendingAsync(db, cancellationToken);
        }

        _logger.LogDebug("Processing {Count} outbox messages from {Source} (SKIP LOCKED)",
            messages.Count, source);

        await PublishBatchAsync(messages, publisher, source, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await CountPendingAsync(db, cancellationToken);
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

    private async Task PublishBatchAsync(
        List<OutboxMessage> messages,
        IPublishEndpoint publisher,
        string source,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            try
            {
                var type = Type.GetType(message.Type);
                if (type == null)
                {
                    HandleFailure(message, source, $"Type not found: {message.Type}", exception: null);
                    continue;
                }

                var payload = JsonSerializer.Deserialize(message.Content, type);
                if (payload is IIntegrationEvent integrationEvent)
                {
                    // Per-publish timeout: fail fast when MassTransit bus is unavailable
                    // (e.g., RabbitMQ not reachable) rather than blocking indefinitely.
                    using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    publishCts.CancelAfter(PublishTimeout);
                    await publisher.Publish(integrationEvent, type, publishCts.Token);
                }

                var publishedAt = DateTime.UtcNow;
                // T1.6 — record (publish-time - occurrence-time) so the lag
                // histogram surfaces "outbox is healthy" vs "outbox is lagging
                // 5 minutes behind" without operators reading log files.
                _metrics.RecordOutboxAge((publishedAt - message.OccurredOnUtc).TotalSeconds);
                message.MarkAsProcessed(publishedAt);
            }
            catch (Exception ex)
            {
                HandleFailure(message, source, ex.Message, ex);
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
