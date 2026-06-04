using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Handles trip-level failures for envelope-dispatched orders. Legacy
/// trips don't fire TripFailedIntegrationEvent (their failure path is
/// per-task), so this consumer ignores any event with a null VendorUpperKey.
/// </summary>
public class TripFailedConsumer : IConsumer<TripFailedIntegrationEvent>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<TripFailedConsumer> _logger;

    public TripFailedConsumer(IDeliveryOrderRepository repository, ILogger<TripFailedConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripFailedIntegrationEvent> context)
    {
        var evt = context.Message;

        if (string.IsNullOrEmpty(evt.VendorUpperKey))
        {
            _logger.LogDebug("TripFailed event has no VendorUpperKey; legacy flow uses per-task events instead — skipping.");
            return;
        }

        _logger.LogInformation(
            "Received TripFailed event for Trip {TripId} (envelope upperKey {UpperKey}): {Reason}",
            evt.TripId, evt.VendorUpperKey, evt.Reason);

        var order = await _repository.GetByIdAsync(evt.DeliveryOrderId, context.CancellationToken);
        if (order is null)
        {
            _logger.LogWarning("No DeliveryOrder found for DeliveryOrderId {DeliveryOrderId} (TripId {TripId}). Skipping.", evt.DeliveryOrderId, evt.TripId);
            return;
        }

        try
        {
            order.MarkVendorFailed(evt.Reason);
            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("DeliveryOrder {OrderId} marked Failed via Trip {TripId}: {Reason}",
                order.Id, evt.TripId, evt.Reason);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Cannot fail DeliveryOrder {OrderId}: {Message}", order.Id, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrency conflict failing DeliveryOrder {OrderId}. MassTransit will retry.", order.Id);
            throw;
        }
    }
}
