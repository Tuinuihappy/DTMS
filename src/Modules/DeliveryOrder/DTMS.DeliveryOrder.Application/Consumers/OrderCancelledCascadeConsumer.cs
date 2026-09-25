using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Dispatch.Application.Commands.CancelTrip;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Auth;
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
    private readonly ICurrentActorContext _actor;
    private readonly ILogger<OrderCancelledCascadeConsumer> _logger;

    public OrderCancelledCascadeConsumer(
        ITripRepository tripRepository,
        ISender sender,
        ICurrentActorContext actor,
        ILogger<OrderCancelledCascadeConsumer> logger)
    {
        _tripRepository = tripRepository;
        _sender = sender;
        _actor = actor;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderCancelledIntegrationEventV1> context)
    {
        var evt = context.Message;

        // Active = not-yet-terminal trips of this order. Created /
        // InProgress / Hang / Held all qualify; Completed/Failed/Cancelled
        // are already done.
        var trips = await _tripRepository.GetByDeliveryOrderIdAsync(evt.DeliveryOrderId, context.CancellationToken);
        var activeTrips = trips
            .Where(t => t.Status is TripStatus.Created or TripStatus.InProgress or TripStatus.Hang or TripStatus.Held)
            .ToList();

        if (activeTrips.Count == 0)
        {
            _logger.LogDebug("[CascadeCancel] Order {OrderId}: no active trips to cascade.", evt.DeliveryOrderId);
            return;
        }

        // Who asked for this cancel, split the way the trip audit wants it:
        // ActionBy is a person, Origin is a system. The event carries one field
        // for both — TriggeredBy is PrincipalId with its kind prefix stripped,
        // so it reads "86347852" for an operator and "oms" for a source system —
        // and Channel is what tells the two apart.
        //
        // Deliberately NOT evt.SourceSystem: that is the order's OWNER, not the
        // caller. An operator cancelling an OMS order would tag every cascaded
        // trip via=oms, a lie the audit row has no way to correct later.
        //
        // Channel is null only on events queued before schema 1.2; TryParse
        // fails there and we treat the caller as human, which is what those
        // events meant.
        var isSystemCaller = Enum.TryParse<SourceChannel>(evt.Channel, out var channel)
                          && channel == SourceChannel.SystemApi;
        var origin = isSystemCaller ? evt.TriggeredBy : null;
        var actionBy = isSystemCaller ? null : evt.TriggeredBy;
        // ActorContext's no-principal fallback, not a person — an audit row
        // reading "cancelled by system" is worse than one reading nothing.
        if (actionBy == ActorContext.System.Source) actionBy = null;

        // Re-enter the caller's identity for the nested cancels. Without this the
        // Dispatch mapper stamps TripCancelledIntegrationEvent.TriggeredBy from
        // the ambient actor, which inside a consumer is ActorContext.System — so
        // the one cascade row the order timeline actually renders would read
        // "system" no matter who asked. The mapper runs in the SaveChanges
        // interceptor, i.e. inside this async flow, so the AsyncLocal reaches it.
        using var scope = _actor.BeginScope(new ActorContext(
            principalId: string.IsNullOrWhiteSpace(evt.TriggeredBy)
                ? string.Empty
                : (isSystemCaller ? $"system:{evt.TriggeredBy}" : $"user:{evt.TriggeredBy}"),
            type: isSystemCaller ? PrincipalType.System : PrincipalType.User,
            displayName: evt.DisplayName ?? string.Empty,
            channel: isSystemCaller ? SourceChannel.SystemApi : SourceChannel.InternalJob,
            onBehalfOf: null,
            correlationId: evt.CorrelationId));

        var cancelled = 0;
        var failed = 0;
        foreach (var trip in activeTrips)
        {
            // ActedAt stays null: the cascade fires at consume time, and the
            // caller's upstream timestamp is not on this event. Trip.Cancel
            // falls back to receive time.
            var cmd = new CancelTripCommand(
                trip.Id, $"Order cancelled: {evt.Reason}",
                ActionBy: actionBy, Origin: origin);
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
