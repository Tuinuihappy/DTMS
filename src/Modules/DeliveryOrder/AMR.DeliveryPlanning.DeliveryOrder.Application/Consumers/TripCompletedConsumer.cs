using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Finalizes a DeliveryOrder when one of its Trips completes. The trip's
/// items are marked Delivered, then the order's status is recomputed —
/// it stays InProgress while other trips are still in flight, and
/// transitions to Completed / PartiallyCompleted only when every item
/// is terminal. Multi-group orders no longer finalize prematurely.
///
/// Two branches:
///   - Envelope flow (non-null VendorUpperKey): per-trip item update,
///     then derive order status from item states.
///   - Legacy flow (null VendorUpperKey): POD-driven, kept on the
///     original MarkAsCompleted path for backwards compatibility.
/// </summary>
public class TripCompletedConsumer : IConsumer<TripCompletedIntegrationEvent>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<TripCompletedConsumer> _logger;

    public TripCompletedConsumer(IDeliveryOrderRepository repository, ILogger<TripCompletedConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripCompletedIntegrationEvent> context)
    {
        var evt = context.Message;
        var isEnvelope = !string.IsNullOrEmpty(evt.VendorUpperKey);
        _logger.LogInformation(
            "Received TripCompleted event for Trip {TripId}, Job {JobId} (envelope: {Envelope}, vendorUpperKey: {UpperKey})",
            evt.TripId, evt.JobId, isEnvelope, evt.VendorUpperKey ?? "(none)");

        var order = await _repository.GetByIdAsync(evt.DeliveryOrderId, context.CancellationToken);
        if (order is null)
        {
            _logger.LogWarning("No DeliveryOrder found for DeliveryOrderId {DeliveryOrderId} (TripId {TripId}). Skipping.", evt.DeliveryOrderId, evt.TripId);
            return;
        }

        try
        {
            if (isEnvelope)
            {
                // POD policy gate: when Order.RequiresDropPod is true the
                // operator must scan each item via /pod-scan to land it
                // at Delivered. Items stay at DroppedOff (or Picked, if
                // the drop SUB_TASK_FINISHED didn't fire) — the order
                // stays InProgress while it waits. Template-level
                // default is wired in via a future cross-module reader
                // (Order.RequiresDropPod overrides regardless).
                // NOTE: Currently uses Order.RequiresDropPod only — template
                // resolution is deferred to a later iteration so this
                // consumer stays read-only against Planning.
                var templateDefault = false;
                var delivered = order.MarkTripItemsDeliveredOrLeaveForPod(evt.TripId, templateDefault);

                // Legacy fallback: pre-Option-D rows have Item.TripId null
                // and won't match a TripId-keyed update. Fall back to the
                // old "mark whole order" semantic only when the per-trip
                // update affected nothing AND there's no other trip-bound
                // item in the order (i.e. the order pre-dates retry/binding).
                if (delivered == 0 && !order.Items.Any(i => i.TripId.HasValue) && order.RequiresDropPod is not true)
                {
                    _logger.LogWarning(
                        "[Legacy fallback] Trip {TripId} affected no items on Order {OrderId} — pre-binding row. " +
                        "Falling back to MarkVendorCompleted.",
                        evt.TripId, order.Id);
                    order.MarkVendorCompleted();
                }
                else
                {
                    order.RecomputeStatusFromItems();
                }
            }
            else
            {
                order.MarkAsCompleted();
            }

            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("DeliveryOrder {OrderId} status after Trip {TripId} {Flow}: {Status}",
                order.Id, evt.TripId, isEnvelope ? "(envelope)" : "(legacy)", order.Status);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Cannot finalize DeliveryOrder {OrderId}: {Message}", order.Id, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrency conflict finalizing DeliveryOrder {OrderId}. MassTransit will retry.", order.Id);
            throw;
        }
    }
}
