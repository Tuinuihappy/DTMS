using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Dispatch.Application.Commands.CancelTrip;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Consumers;

/// <summary>
/// Cascade an order-level Cancel down to every in-flight Trip. Reads
/// the order's Trips and dispatches a CancelTripCommand per trip — that
/// command calls the vendor's cancel API, so the robots actually stop
/// (not just DTMS-side bookkeeping). Best-effort: a per-trip vendor
/// failure is logged but doesn't abort the cascade — the reconciler
/// will eventually heal stragglers.
///
/// Subscribes to DeliveryOrderCancelledIntegrationEventV1 so the
/// cascade rides the outbox path — if the host crashes mid-cascade,
/// MassTransit redelivers and Trip.Cancel is idempotent on retry.
/// </summary>
public class OrderCancelledCascadeConsumer : IConsumer<DeliveryOrderCancelledIntegrationEventV1>
{
    private readonly ITripRepository _tripRepository;
    private readonly ISender _sender;
    private readonly ILogger<OrderCancelledCascadeConsumer> _logger;

    public OrderCancelledCascadeConsumer(
        ITripRepository tripRepository,
        ISender sender,
        ILogger<OrderCancelledCascadeConsumer> logger)
    {
        _tripRepository = tripRepository;
        _sender = sender;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderCancelledIntegrationEventV1> context)
    {
        var evt = context.Message;

        // Active = not-yet-terminal trips of this order. Created /
        // InProgress / Paused all qualify; Completed/Failed/Cancelled
        // are already done.
        var trips = await _tripRepository.GetByDeliveryOrderIdAsync(evt.DeliveryOrderId, context.CancellationToken);
        var activeTrips = trips
            .Where(t => t.Status is TripStatus.Created or TripStatus.InProgress or TripStatus.Paused)
            .ToList();

        if (activeTrips.Count == 0)
        {
            _logger.LogDebug("[CascadeCancel] Order {OrderId}: no active trips to cascade.", evt.DeliveryOrderId);
            return;
        }

        var cancelled = 0;
        var failed = 0;
        foreach (var trip in activeTrips)
        {
            var cmd = new CancelTripCommand(trip.Id, $"Order cancelled: {evt.Reason}");
            var result = await _sender.Send(cmd, context.CancellationToken);
            if (result.IsSuccess) cancelled++;
            else
            {
                failed++;
                _logger.LogWarning(
                    "[CascadeCancel] Trip {TripId} (Order {OrderId}) cascade-cancel failed: {Error} — reconciler will retry.",
                    trip.Id, evt.DeliveryOrderId, result.Error);
            }
        }

        _logger.LogInformation(
            "[CascadeCancel] Order {OrderId}: cancelled {OK}/{Total} trips (failed: {Failed})",
            evt.DeliveryOrderId, cancelled, activeTrips.Count, failed);
    }
}
