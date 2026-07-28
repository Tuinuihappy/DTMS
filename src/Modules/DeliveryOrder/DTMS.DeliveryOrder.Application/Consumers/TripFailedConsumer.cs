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
/// TripRejected (vendor refused pre-execution) propagates identically —
/// both event types funnel into the same handler body.
/// </summary>
public class TripFailedConsumer :
    IConsumer<TripFailedIntegrationEvent>,
    IConsumer<TripRejectedIntegrationEventV1>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<TripFailedConsumer> _logger;

    public TripFailedConsumer(IDeliveryOrderRepository repository, ILogger<TripFailedConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> context)
        => HandleAsync(context, context.Message.TripId, context.Message.DeliveryOrderId,
            context.Message.VendorUpperKey, context.Message.Reason, eventName: "TripFailed");

    public Task Consume(ConsumeContext<TripRejectedIntegrationEventV1> context)
        => HandleAsync(context, context.Message.TripId, context.Message.DeliveryOrderId,
            context.Message.VendorUpperKey, context.Message.Reason, eventName: "TripRejected");

    private async Task HandleAsync(
        ConsumeContext context, Guid tripId, Guid deliveryOrderId,
        string vendorUpperKey, string reason, string eventName)
    {
        if (string.IsNullOrEmpty(vendorUpperKey))
        {
            _logger.LogDebug("{EventName} event has no VendorUpperKey; legacy flow uses per-task events instead — skipping.", eventName);
            return;
        }

        _logger.LogInformation(
            "Received {EventName} event for Trip {TripId} (envelope upperKey {UpperKey}): {Reason}",
            eventName, tripId, vendorUpperKey, reason);

        var order = await _repository.GetByIdAsync(deliveryOrderId, context.CancellationToken);
        if (order is null)
        {
            _logger.LogWarning("No DeliveryOrder found for DeliveryOrderId {DeliveryOrderId} (TripId {TripId}). Skipping.", deliveryOrderId, tripId);
            return;
        }

        try
        {
            var failed = order.MarkTripItemsFailed(tripId, reason);

            // Legacy fallback for pre-binding rows.
            if (failed == 0 && !order.Items.Any(i => i.TripId.HasValue))
            {
                _logger.LogWarning(
                    "[Legacy fallback] Trip {TripId} affected no items on Order {OrderId} — pre-binding row. " +
                    "Falling back to MarkVendorFailed.",
                    tripId, order.Id);
                order.MarkVendorFailed(reason);
            }
            else
            {
                order.RecomputeStatusFromItems();
            }

            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("DeliveryOrder {OrderId} status after Trip {TripId} failure: {Status}",
                order.Id, tripId, order.Status);
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
