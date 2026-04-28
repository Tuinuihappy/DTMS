using AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip;
using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;
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
    private readonly TenantContext _tenantContext;

    public PlanCommittedConsumer(ISender sender, ILogger<PlanCommittedConsumer> logger, TenantContext tenantContext)
    {
        _sender = sender;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task Consume(ConsumeContext<PlanCommittedIntegrationEvent> context)
    {
        var evt = context.Message;
        _tenantContext.Set(evt.TenantId);
        _logger.LogInformation("Received PlanCommitted event for Job {JobId} with {LegCount} legs, creating Trip...", evt.JobId, evt.Legs.Count);

        var legs = evt.Legs
            .Select(l => new DispatchLegInfo(l.FromStationId, l.ToStationId, l.SequenceOrder))
            .ToList();

        var command = new DispatchTripCommand(evt.JobId, evt.VehicleId, legs);

        var result = await _sender.Send(command, context.CancellationToken);

        if (result.IsSuccess)
            _logger.LogInformation("Trip {TripId} created for Job {JobId}", result.Value, evt.JobId);
        else
            _logger.LogWarning("Failed to create Trip for Job {JobId}: {Error}", evt.JobId, result.Error);
    }
}
