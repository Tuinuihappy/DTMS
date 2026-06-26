using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Releases items from a Cancelled Trip without marking them terminal —
/// the operator may /retry, in which case the new Trip rebinds the same
/// items. The order's terminal status is recomputed in case the cancel
/// was the last outstanding trip on a multi-group order.
///
/// Phase b11: cascade Order→Cancelled when this Trip was the last
/// active one for the order. Prior to b11 the order stuck at Dispatched
/// indefinitely after every Trip went terminal-Cancelled, because items
/// were released back to Pending (waiting for an /retry that never came)
/// and RecomputeStatusFromItems explicitly skips Pending in-flight items.
/// </summary>
public class TripCancelledConsumer : IConsumer<TripCancelledIntegrationEvent>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ITripRepository _tripRepository;
    private readonly ILogger<TripCancelledConsumer> _logger;

    public TripCancelledConsumer(
        IDeliveryOrderRepository repository,
        ITripRepository tripRepository,
        ILogger<TripCancelledConsumer> logger)
    {
        _repository = repository;
        _tripRepository = tripRepository;
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

            // Phase b11 cascade: if this was the last active Trip for the
            // order AND the order is still in-flight, terminate the order
            // too. Without this, multi-group orders whose every trip went
            // Cancelled would sit at Dispatched forever, because items
            // released back to Pending count as in-flight in Recompute.
            // The Order.Cancel call below raises DeliveryOrderCancelled
            // which OrderCancelledCascadeConsumer also handles — but with
            // 0 active trips remaining the cascade will noop, so no loop.
            var cascaded = false;
            if (IsOrderInFlight(order.Status))
            {
                var siblingTrips = await _tripRepository.GetByDeliveryOrderIdAsync(
                    order.Id, context.CancellationToken);
                var hasActiveSibling = siblingTrips.Any(t =>
                    t.Id != evt.TripId &&
                    t.Status is TripStatus.Created or TripStatus.InProgress or TripStatus.Paused);

                if (!hasActiveSibling)
                {
                    order.Cancel(
                        $"All trips for order have been cancelled (last: trip {evt.TripId}, reason: {evt.Reason})");
                    cascaded = true;
                }
            }

            // If the order is admin-cancelled, the cascade reached here AFTER
            // Order.Cancel() ran upstream. Items must follow the order to a
            // terminal state — leaving them Pending strands them forever
            // because the order won't dispatch again.
            var cancelledItems = 0;
            if (order.Status is OrderStatus.Cancelled or OrderStatus.Rejected)
                cancelledItems = order.CancelUnboundItems();

            await _repository.SaveChangesAsync(context.CancellationToken);

            if (released > 0 || cascaded)
                _logger.LogInformation(
                    "DeliveryOrder {OrderId} released {Count} items from cancelled Trip {TripId}; status now {Status} (cascaded: {Cascaded}, cancelled {CancelledItems} unbound items)",
                    order.Id, released, evt.TripId, order.Status, cascaded, cancelledItems);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrency conflict on Order {OrderId} after Trip {TripId} cancel. MassTransit will retry.",
                order.Id, evt.TripId);
            throw;
        }
    }

    // Order states from which a Trip cancel may still cascade Order to
    // Cancelled. Terminal / admin-overriden states are excluded — those
    // either already settled or are handled by a separate flow.
    private static bool IsOrderInFlight(OrderStatus status) => status is
        OrderStatus.Confirmed or OrderStatus.Planning or OrderStatus.Planned or
        OrderStatus.Dispatched or OrderStatus.InProgress;
}
