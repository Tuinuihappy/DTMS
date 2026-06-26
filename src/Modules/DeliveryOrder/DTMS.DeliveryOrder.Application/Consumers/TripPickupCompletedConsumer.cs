using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Flips items bound to the trip from Pending → Picked when the vendor
/// reports the robot has finished its pickup action at the trip's
/// pickup station. The trip stays InProgress (the trip-level status is
/// vendor-driven and doesn't change here) — this is purely an
/// item-level signal so operators can distinguish "robot allocated but
/// hasn't picked up yet" from "items are physically on the robot, in
/// transit to the drop station".
///
/// Idempotent end-to-end:
///   • Duplicate webhooks at the pickup station → Trip.MarkVendorPickedUp
///     re-fires the event, but Order.MarkTripItemsPicked is a no-op
///     when items are already Picked / Delivered / Failed.
///   • Race with TripCompletedConsumer (TASK_FINISHED arrives before
///     this consumer wakes): MarkTripItemsDelivered moved items past
///     Picked, so MarkTripItemsPicked finds nothing to do.
/// </summary>
public class TripPickupCompletedConsumer : IConsumer<TripPickupCompletedIntegrationEvent>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<TripPickupCompletedConsumer> _logger;

    public TripPickupCompletedConsumer(
        IDeliveryOrderRepository repository,
        ILogger<TripPickupCompletedConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripPickupCompletedIntegrationEvent> context)
    {
        var evt = context.Message;
        if (evt.DeliveryOrderId == Guid.Empty)
        {
            _logger.LogDebug("[TripPickup] Trip {TripId} has no DeliveryOrderId; skipping.",
                evt.TripId);
            return;
        }

        var order = await _repository.GetByIdAsync(evt.DeliveryOrderId, context.CancellationToken);
        if (order is null)
        {
            _logger.LogWarning("[TripPickup] No DeliveryOrder for {OrderId} (Trip {TripId}); skipping.",
                evt.DeliveryOrderId, evt.TripId);
            return;
        }

        var picked = order.MarkTripItemsPicked(evt.TripId);
        if (picked == 0) return;   // idempotent — nothing changed

        try
        {
            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("[TripPickup] Order {OrderId} Trip {TripId}: {Picked} items Pending → Picked",
                order.Id, evt.TripId, picked);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[TripPickup] Concurrency conflict on Order {OrderId} — MassTransit will retry.",
                order.Id);
            throw;
        }
    }
}
