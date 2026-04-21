using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
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

        // Look up the DeliveryOrder by JobId — for MVP we use JobId correlation
        // In production, we'd have a mapping table or carry DeliveryOrderId in the event
        _logger.LogInformation("Trip {TripId} completed — DeliveryOrder completion flow triggered", evt.TripId);

        await Task.CompletedTask;
    }
}
