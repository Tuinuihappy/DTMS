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
        totalPending += await ProcessOutboxAsync<OutboxDbContext>(scope, publisher, "outbox", cancellationToken);
        totalPending += await ProcessOutboxAsync<DeliveryOrderDbContext>(scope, publisher, DeliveryOrderDbContext.Schema, cancellationToken);
        totalPending += await ProcessOutboxAsync<PlanningDbContext>(scope, publisher, PlanningDbContext.Schema, cancellationToken);
        totalPending += await ProcessOutboxAsync<DispatchDbContext>(scope, publisher, DispatchDbContext.Schema, cancellationToken);
        totalPending += await ProcessOutboxAsync<FleetDbContext>(scope, publisher, FleetDbContext.Schema, cancellationToken);
        totalPending += await ProcessOutboxAsync<VendorAdapterDbContext>(scope, publisher, VendorAdapterDbContext.Schema, cancellationToken);

        _metrics.SetOutboxPending(totalPending);
    }

    private async Task<long> ProcessOutboxAsync<TDbContext>(
        IServiceScope scope,
        IPublishEndpoint publisher,
        string source,
        CancellationToken cancellationToken)
        where TDbContext : DbContext
    {
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var now = DateTime.UtcNow;

        var messages = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null
                        && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= now))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            // No batch this tick — query the residual pending count for the gauge.
            return await db.Set<OutboxMessage>()
                .CountAsync(m => m.ProcessedOnUtc == null, cancellationToken);
        }

        _logger.LogDebug("Processing {Count} outbox messages from {Source}", messages.Count, source);

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
                    publishCts.CancelAfter(TimeSpan.FromSeconds(10));
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

        await db.SaveChangesAsync(cancellationToken);

        // Residual pending after the batch (some messages may have failed and
        // gone back to NextRetryAtUtc in the future — they still count as
        // backlog for the gauge).
        return await db.Set<OutboxMessage>()
            .CountAsync(m => m.ProcessedOnUtc == null, cancellationToken);
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
