using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Reacts to the vendor's "robot finished drop action" signal.
///
/// When Order.RequiresDropPod is false (or null with template-default
/// false) items land at Delivered immediately — at the drop station the
/// item is physically at the consignee, so "Delivered" is semantically
/// correct and the operator dashboard doesn't have to wait for the
/// vendor's bookkeeping TASK_FINISHED (which can lag while the robot
/// returns to base or starts the next leg). RecomputeStatusFromItems()
/// then lets the order finalize early too.
///
/// When RequiresDropPod is true items land at DroppedOff, holding the
/// order InProgress until operator /pod-scan transitions them to
/// Delivered.
///
/// Idempotent end-to-end: MarkTripItemsDelivered + MarkTripItemsDroppedOff
/// both skip items already past their target state, so a late
/// TripCompleted still safely no-ops on this path.
///
/// Source of truth for the policy is the loaded Order itself — the event
/// also carries RequiresDropPod as an audit hint, but the live order
/// wins if they disagree (e.g. ops flipped the flag between drop and
/// processing).
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

        // Live order is authoritative. Event flag (evt.RequiresDropPod) is
        // a snapshot taken at webhook-receive time — used here only for
        // the trace log so an audit can detect mid-flight policy changes.
        var requiresPod = order.RequiresDropPod ?? false;
        int affected;
        string transition;
        if (requiresPod)
        {
            affected = order.MarkTripItemsDroppedOff(evt.TripId);
            transition = "Picked → DroppedOff (POD required)";
        }
        else
        {
            affected = order.MarkTripItemsDelivered(evt.TripId);
            transition = "Picked → Delivered (no POD)";
            if (affected > 0) order.RecomputeStatusFromItems();
        }

        if (affected == 0) return;

        try
        {
            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation(
                "[TripDrop] Order {OrderId} Trip {TripId}: {Affected} items {Transition} (event flag: {EventFlag}, live flag: {LiveFlag}) — order now {Status}",
                order.Id, evt.TripId, affected, transition,
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
