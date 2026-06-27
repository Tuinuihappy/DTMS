using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Consumers;

/// <summary>
/// Handles trip-level failures for envelope-dispatched orders. The
/// failed trip's items are marked Failed, then the order's status is
/// recomputed. The order transitions to Failed only when ALL trips
/// failed; mixed outcomes yield PartiallyCompleted on the final tally.
/// Legacy trips (null VendorUpperKey) fail per-task and are ignored.
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
            var failed = order.MarkTripItemsFailed(evt.TripId, evt.Reason);

            // Legacy fallback for pre-binding rows.
            if (failed == 0 && !order.Items.Any(i => i.TripId.HasValue))
            {
                _logger.LogWarning(
                    "[Legacy fallback] Trip {TripId} affected no items on Order {OrderId} — pre-binding row. " +
                    "Falling back to MarkVendorFailed.",
                    evt.TripId, order.Id);
                order.MarkVendorFailed(evt.Reason);
            }
            else
            {
                order.RecomputeStatusFromItems();
            }

            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("DeliveryOrder {OrderId} status after Trip {TripId} failure: {Status}",
                order.Id, evt.TripId, order.Status);
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
