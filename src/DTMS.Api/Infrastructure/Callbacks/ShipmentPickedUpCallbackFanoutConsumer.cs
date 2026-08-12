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
/// 2026-08 — fans <see cref="TripPickupCompletedIntegrationEvent"/> out to the
/// order's source system as <c>shipment.pickedup.v1</c> — the first outbound
/// callback on the pickup lifecycle (previously in-DTMS only). The OMS
/// formatter targets <c>POST /integrations/tms/shipments/{id}/pickup-arrived</c>
/// with <c>{orderRef, locationCode, occurredAt}</c>.
///
/// <para>Per-mode semantics: AMR = robot arrived at the pickup dock (RIOT3's
/// MOVE-FINISHED at the pickup station — the only pre-completion signal it
/// emits, and it matches OMS's "pickup-arrived" name); Manual = operator
/// confirmed loading, geofence-verified (deliberately NOT skipped, unlike the
/// arrived fan-out's legacy Manual skip); self-managed = SKIPPED — its pickup
/// auto-fires at dispatch milliseconds after started and carries no physical
/// meaning (the source system executes the transport itself).</para>
///
/// <para>Enrichment: shipmentId = root trip id (retry chain), locationCode =
/// the pickup code the source system submitted on the order items
/// (Item.PickupLocationCode — round-trips the subscriber's own vocabulary),
/// occurredAt = Trip.VendorPickedUpAt (AMR: vendor mission ChangeStateTime,
/// survives delayed webhooks and reconciler catch-ups).</para>
/// </summary>
public sealed class ShipmentPickedUpCallbackFanoutConsumer
    : IConsumer<TripPickupCompletedIntegrationEvent>
{
    private readonly ISubscriptionLookup _lookup;
    private readonly IServiceProvider _sp;
    private readonly OutboxDbContext _outbox;
    private readonly ITripRepository _trips;
    private readonly IDeliveryOrderRepository _orders;
    private readonly ILogger<ShipmentPickedUpCallbackFanoutConsumer> _log;

    public ShipmentPickedUpCallbackFanoutConsumer(
        ISubscriptionLookup lookup,
        IServiceProvider sp,
        OutboxDbContext outbox,
        ITripRepository trips,
        IDeliveryOrderRepository orders,
        ILogger<ShipmentPickedUpCallbackFanoutConsumer> log)
    {
        _lookup = lookup;
        _sp = sp;
        _outbox = outbox;
        _trips = trips;
        _orders = orders;
        _log = log;
    }

    public async Task Consume(ConsumeContext<TripPickupCompletedIntegrationEvent> ctx)
    {
        var ct = ctx.CancellationToken;
        var evt = ctx.Message;
        const string eventType = CallbackEventTypes.ShipmentPickedUpV1;

        if (evt.DeliveryOrderId == Guid.Empty) return;

        var order = await _orders.GetByIdAsync(evt.DeliveryOrderId, ct);
        if (order is null || string.IsNullOrWhiteSpace(order.OrderRef))
            return;   // internal/draft order — no upstream to notify

        // Self-managed pickup is auto-fired at dispatch (SelfManagedDispatchStrategy)
        // — not a physical signal; the source system did the pickup itself.
        if (order.SelfManaged)
        {
            _log.LogInformation(
                "[ShipmentPickedUp] Order {OrderId} is self-managed — pickup is source-executed; skipping.",
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
        var locationCode = trip?.PickupLocationCode;
        if (locationCode is null)
        {
            locationCode = order.Items
                .Where(i => i.TripId == evt.TripId)
                .Select(i => i.PickupLocationCode)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .FirstOrDefault();
            if (locationCode is not null)
                _log.LogInformation(
                    "[ShipmentPickedUp] Trip {TripId} has no denormalized pickup code (legacy trip?) — fell back to item scan.",
                    evt.TripId);
        }
        if (locationCode is null)
        {
            _log.LogWarning(
                "[ShipmentPickedUp] Order {OrderId} Trip {TripId} has no pickup code on the trip nor on bound items — skipping (locationCode is required by the endpoint).",
                order.Id, evt.TripId);
            return;
        }
        var shipmentId = (await _trips.GetRootTripIdAsync(evt.TripId, ct)).ToString();
        // Business event time, not send time: VendorPickedUpAt carries the
        // vendor's ChangeStateTime for AMR (correct even when the webhook was
        // delayed or the reconciler caught the miss); OccurredOn is only the
        // moment DTMS observed the transition.
        var occurredAt = trip?.VendorPickedUpAt ?? evt.OccurredOn;
        var context = new ShipmentPickedUpContext(shipmentId, order.OrderRef!, locationCode, occurredAt);
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
                "[ShipmentPickedUp] Fanned out {EventType} (order {OrderId}, trip {TripId}, location {LocationCode}) to {N} subscriber(s)",
                eventType, order.Id, evt.TripId, locationCode, subs.Count);
        }
        catch (DbUpdateException ex) when (CallbackFanout.IsUniqueViolation(ex))
        {
            _log.LogInformation(
                "[ShipmentPickedUp] Outbox rows for order={OrderId} trip={TripId} correlation={CorrelationId} already enqueued; skipping duplicate.",
                order.Id, evt.TripId, correlationId);
        }
    }
}
