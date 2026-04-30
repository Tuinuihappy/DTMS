using System.Text.Json;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Api.Infrastructure.Outbox;

public class OutboxProcessorService : BackgroundService, IOutboxProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorService> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    public OutboxProcessorService(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
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
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0) return;

        _logger.LogDebug("Processing {Count} outbox messages", messages.Count);

        foreach (var message in messages)
        {
            try
            {
                var type = Type.GetType(message.Type);
                if (type == null)
                {
                    _logger.LogWarning("Cannot resolve type {Type} for outbox message {Id}", message.Type, message.Id);
                    message.MarkAsFailed(DateTime.UtcNow, $"Type not found: {message.Type}");
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

                message.MarkAsProcessed(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish outbox message {Id}", message.Id);
                message.MarkAsFailed(DateTime.UtcNow, ex.Message);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
