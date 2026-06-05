using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Releases items from a Cancelled Trip without marking them terminal —
/// the operator may /retry, in which case the new Trip rebinds the same
/// items. The order's terminal status is recomputed in case the cancel
/// was the last outstanding trip on a multi-group order.
/// </summary>
public class TripCancelledConsumer : IConsumer<TripCancelledIntegrationEvent>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<TripCancelledConsumer> _logger;

    public TripCancelledConsumer(IDeliveryOrderRepository repository, ILogger<TripCancelledConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripCancelledIntegrationEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation(
            "Received TripCancelled event for Trip {TripId} (upperKey {UpperKey}): {Reason}",
            evt.TripId, evt.VendorUpperKey ?? "(none)", evt.Reason);

        var order = await _repository.GetByIdAsync(evt.DeliveryOrderId, context.CancellationToken);
        if (order is null)
        {
            _logger.LogWarning(
                "No DeliveryOrder found for DeliveryOrderId {DeliveryOrderId} (TripId {TripId}). Skipping.",
                evt.DeliveryOrderId, evt.TripId);
            return;
        }

        try
        {
            // Items stay Pending — they're waiting for a potential retry.
            // Just clear the trip binding so the next dispatch finds them.
            var released = order.UnassignItemsFromTrip(evt.TripId);

            // A cancel may complete the order if every other trip already
            // finalized (e.g. multi-group with no outstanding items).
            order.RecomputeStatusFromItems();

            await _repository.SaveChangesAsync(context.CancellationToken);

            if (released > 0)
                _logger.LogInformation(
                    "DeliveryOrder {OrderId} released {Count} items from cancelled Trip {TripId}; status now {Status}",
                    order.Id, released, evt.TripId, order.Status);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrency conflict on Order {OrderId} after Trip {TripId} cancel. MassTransit will retry.",
                order.Id, evt.TripId);
            throw;
        }
    }
}
