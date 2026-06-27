using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Consumers;

/// <summary>
/// Drives Order.Status from Dispatched (or any earlier in-flight state)
/// → InProgress when the first trip transitions to InProgress on the
/// vendor side. Idempotent via Order.MarkInProgressIfNotYet — subsequent
/// TripStarted webhooks for additional groups are silently no-op'd.
/// </summary>
public class TripStartedConsumer : IConsumer<TripStartedIntegrationEvent>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<TripStartedConsumer> _logger;

    public TripStartedConsumer(
        IDeliveryOrderRepository repository,
        ILogger<TripStartedConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripStartedIntegrationEvent> context)
    {
        var evt = context.Message;
        if (evt.DeliveryOrderId == Guid.Empty)
        {
            // Legacy path or domain event fired without the field set — nothing to do.
            _logger.LogDebug("[TripStarted] Trip {TripId} has no DeliveryOrderId; skipping order transition.",
                evt.TripId);
            return;
        }

        var order = await _repository.GetByIdAsync(evt.DeliveryOrderId, context.CancellationToken);
        if (order is null)
        {
            _logger.LogWarning("[TripStarted] No DeliveryOrder for {OrderId} (Trip {TripId}); skipping.",
                evt.DeliveryOrderId, evt.TripId);
            return;
        }

        var before = order.Status;
        order.MarkInProgressIfNotYet();
        if (order.Status == before) return;   // already InProgress or admin-blocked

        try
        {
            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation(
                "[TripStarted] Order {OrderId} {Before} → {After} (first Trip started: {TripId})",
                order.Id, before, order.Status, evt.TripId);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[TripStarted] Concurrency conflict on Order {OrderId} — MassTransit will retry.",
                order.Id);
            throw;
        }
    }
}
