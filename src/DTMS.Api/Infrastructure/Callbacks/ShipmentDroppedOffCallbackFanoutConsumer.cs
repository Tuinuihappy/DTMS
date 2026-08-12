using System.Text;
using DTMS.Api.Infrastructure.Outbox;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DTMS.Api.Infrastructure.Callbacks;

/// <summary>
/// 2026-08 — fans <see cref="TripDropCompletedIntegrationEvent"/> out to the
/// order's source system as <c>shipment.droppedoff.v1</c> (renamed from
/// shipment.arrived.v1 when OMS moved the route). The OMS formatter targets
/// <c>POST /integrations/tms/shipments/{id}/dropoff-arrived</c> with
/// <c>{orderRef, locationCode, occurredAt}</c> — the lot list left the wire.
///
/// <para>Per-mode semantics mirror the pickedup fan-out: AMR = robot reached
/// the drop dock; Manual = operator confirmed drop, geofence-verified — the
/// old consumer's blanket TransportMode.Manual skip is GONE, so Manual pool
/// orders send their first-ever drop callback here; self-managed = SKIPPED —
/// their drop is reported INTO DTMS by the source system
/// (POST /api/v1/source/trips/{id}/drop), so sending it back would echo.</para>
///
/// <para>Enrichment: shipmentId = root trip id (retry chain), locationCode =
/// the drop code the source system submitted on the order items
/// (Item.DropLocationCode — round-trips the subscriber's own vocabulary),
/// occurredAt = Trip.VendorDroppedAt (AMR: vendor mission ChangeStateTime,
/// survives delayed webhooks and reconciler catch-ups).</para>
/// </summary>
public sealed class ShipmentDroppedOffCallbackFanoutConsumer
    : IConsumer<TripDropCompletedIntegrationEvent>
{
    private readonly ISubscriptionLookup _lookup;
    private readonly IServiceProvider _sp;
    private readonly OutboxDbContext _outbox;
    private readonly ITripRepository _trips;
    private readonly IDeliveryOrderRepository _orders;
    private readonly ILogger<ShipmentDroppedOffCallbackFanoutConsumer> _log;

    public ShipmentDroppedOffCallbackFanoutConsumer(
        ISubscriptionLookup lookup,
        IServiceProvider sp,
        OutboxDbContext outbox,
        ITripRepository trips,
        IDeliveryOrderRepository orders,
        ILogger<ShipmentDroppedOffCallbackFanoutConsumer> log)
    {
        _lookup = lookup;
        _sp = sp;
        _outbox = outbox;
        _trips = trips;
        _orders = orders;
        _log = log;
    }

    public async Task Consume(ConsumeContext<TripDropCompletedIntegrationEvent> ctx)
    {
        var ct = ctx.CancellationToken;
        var evt = ctx.Message;
        const string eventType = CallbackEventTypes.ShipmentDroppedOffV1;

        if (evt.DeliveryOrderId == Guid.Empty) return;

        var order = await _orders.GetByIdAsync(evt.DeliveryOrderId, ct);
        if (order is null || string.IsNullOrWhiteSpace(order.OrderRef))
            return;   // internal/draft order — no upstream to notify

        // Self-managed drop is reported INTO DTMS by the source system itself
        // — notifying them back would only echo their own report.
        if (order.SelfManaged)
        {
            _log.LogInformation(
                "[ShipmentDroppedOff] Order {OrderId} is self-managed — drop is source-reported; skipping.",
                order.Id);
            return;
        }

        var source = order.SourceSystemKey;
        if (string.IsNullOrWhiteSpace(source)) return;

        var subs = (await _lookup.GetSubscribersAsync(eventType, ct))
            .Where(s => string.Equals(s.SystemKey, source, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (subs.Count == 0) return;   // source system not subscribed → nothing to send

        var trip = await _trips.GetByIdAsync(evt.TripId, ct);

        // locationCode is REQUIRED by the endpoint. Primary source (2026-08):
        // the code frozen onto the Trip at creation — trip-scoped, immune to
        // item unbinding on cancel and to mixed-code item sets. NULL only on
        // legacy trips the backfill couldn't reach or when a creation funnel
        // couldn't resolve a code — fall back to scanning the bound items,
        // and log so a leaking funnel is visible.
        var locationCode = trip?.DropLocationCode;
        if (locationCode is null)
        {
            locationCode = order.Items
                .Where(i => i.TripId == evt.TripId)
                .Select(i => i.DropLocationCode)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .FirstOrDefault();
            if (locationCode is not null)
                _log.LogInformation(
                    "[ShipmentDroppedOff] Trip {TripId} has no denormalized drop code (legacy trip?) — fell back to item scan.",
                    evt.TripId);
        }
        if (locationCode is null)
        {
            _log.LogWarning(
                "[ShipmentDroppedOff] Order {OrderId} Trip {TripId} has no drop code on the trip nor on bound items — skipping (locationCode is required by the endpoint).",
                order.Id, evt.TripId);
            return;
        }
        var shipmentId = (await _trips.GetRootTripIdAsync(evt.TripId, ct)).ToString();
        // Business event time, not send time: VendorDroppedAt carries the
        // vendor's ChangeStateTime for AMR (correct even when the webhook was
        // delayed or the reconciler caught the miss); OccurredOn is only the
        // moment DTMS observed the transition.
        var occurredAt = trip?.VendorDroppedAt ?? evt.OccurredOn;
        var context = new ShipmentDroppedOffContext(shipmentId, order.OrderRef!, locationCode, occurredAt);
        var correlationId = ctx.MessageId ?? Guid.NewGuid();

        foreach (var sub in subs)
        {
            var formatter = _sp.GetRequiredKeyedService<ICallbackPayloadFormatter>(sub.PayloadFormatKey);
            var payload = await formatter.FormatAsync(context, ct);
            _outbox.OutboxMessages.Add(new OutboxMessage(
                id: Guid.NewGuid(),
                type: eventType,
                content: Encoding.UTF8.GetString(payload.Body),
                occurredOnUtc: DateTime.UtcNow,
                partitionKey: sub.SystemKey,
                correlationId: correlationId,
                callbackPath: payload.RelativePath,
                callbackMethod: payload.HttpMethod,
                relatedOrderId: order.Id,
                relatedTripId: evt.TripId));
        }

        try
        {
            await _outbox.SaveChangesAsync(ct);
            _log.LogInformation(
                "[ShipmentDroppedOff] Fanned out {EventType} (order {OrderId}, trip {TripId}, location {LocationCode}) to {N} subscriber(s)",
                eventType, order.Id, evt.TripId, locationCode, subs.Count);
        }
        catch (DbUpdateException ex) when (CallbackFanout.IsUniqueViolation(ex))
        {
            _log.LogInformation(
                "[ShipmentDroppedOff] Outbox rows for order={OrderId} trip={TripId} correlation={CorrelationId} already enqueued; skipping duplicate.",
                order.Id, evt.TripId, correlationId);
        }
    }
}
