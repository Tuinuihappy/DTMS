using AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip;
using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Consumers;

/// <summary>
/// Listens for PlanCommittedIntegrationEvent and auto-creates a Trip.
/// Flow: Planning (Committed) → Dispatch (Create Trip)
/// </summary>
public class PlanCommittedConsumer : IConsumer<PlanCommittedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<PlanCommittedConsumer> _logger;

    public PlanCommittedConsumer(ISender sender, ILogger<PlanCommittedConsumer> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PlanCommittedIntegrationEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Received PlanCommitted event for Job {JobId}, creating Trip...", evt.JobId);

        // For MVP, use placeholder station IDs — in production these would come from the Job's legs
        var command = new DispatchTripCommand(evt.JobId, evt.VehicleId, Guid.Empty, Guid.Empty);

        var result = await _sender.Send(command, context.CancellationToken);

        if (result.IsSuccess)
            _logger.LogInformation("Trip {TripId} created for Job {JobId}", result.Value, evt.JobId);
        else
            _logger.LogWarning("Failed to create Trip for Job {JobId}: {Error}", evt.JobId, result.Error);
    }
}
