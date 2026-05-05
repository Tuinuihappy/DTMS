using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Listens for TripCompletedIntegrationEvent and marks the DeliveryOrder as Completed.
/// Flow: Dispatch (Trip Completed) → DeliveryOrder (Completed)
/// </summary>
public class TripCompletedConsumer : IConsumer<TripCompletedIntegrationEvent>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<TripCompletedConsumer> _logger;

    public TripCompletedConsumer(IDeliveryOrderRepository repository, ILogger<TripCompletedConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripCompletedIntegrationEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Received TripCompleted event for Trip {TripId}, Job {JobId}", evt.TripId, evt.JobId);

        // CONTRACT (MVP): Dispatch sets JobId = DeliveryOrderId when creating a job from a delivery order.
        // TODO: replace with a dedicated job-mapping table when the Dispatch module carries DeliveryOrderId explicitly.
        var order = await _repository.GetByIdAsync(evt.JobId, context.CancellationToken);
        if (order is null)
        {
            _logger.LogWarning("No DeliveryOrder found for JobId {JobId} (TripId {TripId}). Skipping.", evt.JobId, evt.TripId);
            return;
        }

        try
        {
            order.MarkAsCompleted();
            order.UpdateAllItemStatuses(OrderItemStatus.Delivered);
            await _repository.UpdateAsync(order, context.CancellationToken);
            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("DeliveryOrder {OrderId} marked Completed via Trip {TripId}", order.Id, evt.TripId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Cannot complete DeliveryOrder {OrderId}: {Message}", order.Id, ex.Message);
            // Do not re-throw — prevents MassTransit poison-message loop; dead-letter via retry policy.
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrency conflict completing DeliveryOrder {OrderId}. MassTransit will retry.", order.Id);
            throw; // Re-throw so MassTransit retry policy re-fetches and retries.
        }
    }
}
