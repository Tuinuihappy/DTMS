using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Consumers;

/// <summary>
/// Reacts to the vendor's "robot finished drop action" signal by landing
/// the trip's Picked items at DroppedOff — regardless of POD policy, so
/// every item walks the full Pending → Picked → DroppedOff → Delivered
/// flow (2026-10-15; before that a no-POD order jumped straight to
/// Delivered here and finalized early).
///
/// The DroppedOff → Delivered step happens elsewhere:
///   - RequiresDropPod false: TripCompletedConsumer (TASK_FINISHED /
///     source/manual complete) via MarkTripItemsDeliveredOrLeaveForPod.
///   - RequiresDropPod true: operator /pod-scan; TripCompleted leaves the
///     items alone.
/// The order therefore stays in flight until one of those fires — this
/// consumer never recomputes order status.
///
/// Idempotent: MarkTripItemsDroppedOff skips items already DroppedOff or
/// Delivered, so a redelivered or late drop event no-ops. The event's
/// RequiresDropPod flag is logged only, for audit.
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

        var affected = order.MarkTripItemsDroppedOff(evt.TripId);
        if (affected == 0) return;

        try
        {
            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation(
                "[TripDrop] Order {OrderId} Trip {TripId}: {Affected} items Picked → DroppedOff (event flag: {EventFlag}, live flag: {LiveFlag}) — order now {Status}",
                order.Id, evt.TripId, affected,
                evt.RequiresDropPod?.ToString() ?? "(null)",
                order.RequiresDropPod?.ToString() ?? "(null)",
                order.Status);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[TripDrop] Concurrency conflict on Order {OrderId} — MassTransit will retry.", order.Id);
            throw;
        }
    }
}
