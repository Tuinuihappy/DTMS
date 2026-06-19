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

namespace AMR.DeliveryPlanning.Api.Infrastructure.Outbox;

public class OutboxProcessorService : BackgroundService, IOutboxProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkflowMetrics _metrics;
    private readonly ILogger<OutboxProcessorService> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

    public OutboxProcessorService(
        IServiceScopeFactory scopeFactory,
        WorkflowMetrics metrics,
        ILogger<OutboxProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessorService started");

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
        using var scope = _scopeFactory.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        // T1.6 — aggregate pending count across all schemas per poll so the
        // dtms_workflow_outbox_pending gauge reflects current backlog. If this
        // climbs > 500 sustained the outbox is falling behind and ops should
        // page (see plan section 5).
        long totalPending = 0;
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<OutboxDbContext>(), publisher, "outbox", cancellationToken);
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<DeliveryOrderDbContext>(), publisher, DeliveryOrderDbContext.Schema, cancellationToken);
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<PlanningDbContext>(), publisher, PlanningDbContext.Schema, cancellationToken);
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<DispatchDbContext>(), publisher, DispatchDbContext.Schema, cancellationToken);
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<FleetDbContext>(), publisher, FleetDbContext.Schema, cancellationToken);
        totalPending += await ProcessModuleAsync(scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>(), publisher, VendorAdapterDbContext.Schema, cancellationToken);

        _metrics.SetOutboxPending(totalPending);
    }

    private async Task<long> ProcessModuleAsync(
        DbContext db,
        IPublishEndpoint publisher,
        string source,
        CancellationToken cancellationToken)
    {
        var messages = await FetchBatchAsync(db, BatchSize, cancellationToken);

        if (messages.Count == 0)
        {
            // No batch this tick — return residual pending count for the gauge.
            return await CountPendingAsync(db, cancellationToken);
        }

        _logger.LogDebug("Processing {Count} outbox messages from {Source}", messages.Count, source);

        await PublishBatchAsync(messages, publisher, source, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // Residual pending after the batch (some messages may have failed and
        // gone back to NextRetryAtUtc in the future — they still count as
        // backlog for the gauge).
        return await CountPendingAsync(db, cancellationToken);
    }

    // Pulled out of ProcessModuleAsync so Step A3 can branch on Outbox:UseSkipLocked
    // — the SKIP LOCKED path will be a sibling that returns the same shape but uses
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
