using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Flips items bound to the trip from Picked → DroppedOff when the
/// vendor reports the robot finished its drop action at the trip's
/// drop station. Trip.Status stays InProgress (the trip is still
/// "running" from the vendor's perspective until TASK_FINISHED) — this
/// is item-level signal only, so the operator dashboard can show "at
/// dock, awaiting POD" distinct from "in transit".
///
/// Idempotent end-to-end: MarkTripItemsDroppedOff is a no-op when the
/// items are already DroppedOff / Delivered / failed.
/// </summary>
public class TripDropCompletedConsumer : IConsumer<TripDropCompletedIntegrationEvent>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<TripDropCompletedConsumer> _logger;

    public TripDropCompletedConsumer(
        IDeliveryOrderRepository repository,
        ILogger<TripDropCompletedConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripDropCompletedIntegrationEvent> context)
    {
        var evt = context.Message;
        if (evt.DeliveryOrderId == Guid.Empty)
        {
            _logger.LogDebug("[TripDrop] Trip {TripId} has no DeliveryOrderId; skipping.", evt.TripId);
            return;
        }

        var order = await _repository.GetByIdAsync(evt.DeliveryOrderId, context.CancellationToken);
        if (order is null)
        {
            _logger.LogWarning("[TripDrop] No DeliveryOrder for {OrderId} (Trip {TripId}); skipping.",
                evt.DeliveryOrderId, evt.TripId);
            return;
        }

        var dropped = order.MarkTripItemsDroppedOff(evt.TripId);
        if (dropped == 0) return;

        try
        {
            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("[TripDrop] Order {OrderId} Trip {TripId}: {Dropped} items Picked → DroppedOff",
                order.Id, evt.TripId, dropped);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[TripDrop] Concurrency conflict on Order {OrderId} — MassTransit will retry.", order.Id);
            throw;
        }
    }
}
