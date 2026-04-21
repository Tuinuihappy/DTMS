using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Consumers;

/// <summary>
/// Listens for DeliveryOrderReadyForPlanningIntegrationEvent and auto-creates a Job.
/// Flow: DeliveryOrder (Validated) → Planning (Create Job)
/// </summary>
public class DeliveryOrderValidatedConsumer : IConsumer<DeliveryOrderReadyForPlanningIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<DeliveryOrderValidatedConsumer> _logger;

    public DeliveryOrderValidatedConsumer(ISender sender, ILogger<DeliveryOrderValidatedConsumer> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderReadyForPlanningIntegrationEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Received DeliveryOrderReady event for Order {OrderId}, creating Job...", evt.DeliveryOrderId);

        var command = new CreateJobFromOrderCommand(
            evt.DeliveryOrderId,
            evt.PickupStationId,
            evt.DropStationId,
            evt.Priority);

        var result = await _sender.Send(command, context.CancellationToken);

        if (result.IsSuccess)
            _logger.LogInformation("Job {JobId} created for DeliveryOrder {OrderId}", result.Value, evt.DeliveryOrderId);
        else
            _logger.LogWarning("Failed to create Job for DeliveryOrder {OrderId}: {Error}", evt.DeliveryOrderId, result.Error);
    }
}
