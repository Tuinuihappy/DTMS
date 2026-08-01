using System.Text;
using DTMS.Api.Infrastructure.Outbox;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DTMS.Api.Infrastructure.Callbacks;

/// <summary>
/// Phase S.5 (B2) — fans <see cref="TripStartedIntegrationEvent"/> out to the
/// order's source system as <c>shipment.started.v1</c>. Source-routed via
/// subscriptions — only a system that subscribes gets the callback; the OMS
/// formatter targets <c>POST /integrations/tms/shipments/started</c>
/// (2026-08 contract: shipmentId, orderRef, deliveryBy, occurredAt — no lots).
///
/// <para>Enrichment: shipmentId = root trip id (retry chain), orderRef = the
/// order's upstream reference, deliveryBy = vendor vehicle name (self-managed
/// → the order's RequestedBy), occurredAt = Trip.StartedAt. Item binding is
/// NOT required — the payload no longer carries lots, so a trip that starts
/// before binding still notifies. Skips: pool trip (DispatchedAt set — see
/// guard note below), missing vehicle name → fast-cap retry.</para>
/// </summary>
public sealed class ShipmentStartedCallbackFanoutConsumer
    : IConsumer<TripStartedIntegrationEvent>
{
    private readonly ISubscriptionLookup _lookup;
    private readonly IServiceProvider _sp;
    private readonly OutboxDbContext _outbox;
    private readonly ITripRepository _trips;
    private readonly IDeliveryOrderRepository _orders;
    private readonly ILogger<ShipmentStartedCallbackFanoutConsumer> _log;

    public ShipmentStartedCallbackFanoutConsumer(
        ISubscriptionLookup lookup,
        IServiceProvider sp,
        OutboxDbContext outbox,
        ITripRepository trips,
        IDeliveryOrderRepository orders,
        ILogger<ShipmentStartedCallbackFanoutConsumer> log)
    {
        _lookup = lookup;
        _sp = sp;
        _outbox = outbox;
        _trips = trips;
        _orders = orders;
        _log = log;
    }

    public async Task Consume(ConsumeContext<TripStartedIntegrationEvent> ctx)
    {
        // Gated solely by the oms subscription's Enabled (Phase 4 removed the
        // transitional Callbacks:ShipmentEventsEnabled flag) — the subscription
        // lookup below returns nothing when no system subscribes.
        var ct = ctx.CancellationToken;
        var evt = ctx.Message;
        const string eventType = CallbackEventTypes.ShipmentStartedV1;

        if (evt.DeliveryOrderId == Guid.Empty) return;

        var order = await _orders.GetByIdAsync(evt.DeliveryOrderId, ct);
        if (order is null || string.IsNullOrWhiteSpace(order.OrderRef))
            return;   // internal/draft order — no upstream to notify

        var source = order.SourceSystemKey;
        if (string.IsNullOrWhiteSpace(source)) return;

        var subs = (await _lookup.GetSubscribersAsync(eventType, ct))
            .Where(s => string.Equals(s.SystemKey, source, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (subs.Count == 0) return;   // source system not subscribed → nothing to send

        var trip = await _trips.GetByIdAsync(evt.TripId, ct);

        // Pool trips are notified once at dispatch time; the later TripStarted
        // must not re-POST for the same shipment (legacy DispatchedAt guard).
        if (trip?.DispatchedAt is not null)
        {
            _log.LogInformation(
                "[ShipmentStarted] Trip {TripId} — pool trip already notified at dispatch; skipping.",
                evt.TripId);
            return;
        }

        // deliveryBy: AMR → vendor vehicle name; self-managed → RequestedBy.
        // Missing name on a vendor trip = sub-second race with MarkVendorStarted
        // → throw the fast-capped exception so the in-process retry re-reads.
        var requireName = !order.SelfManaged;
        var vehicleName = trip?.VendorVehicleName;
        if (requireName && string.IsNullOrWhiteSpace(vehicleName))
            throw new VendorVehicleUnavailableException(
                $"Trip {evt.TripId} has no VendorVehicleName yet — race with MarkVendorStarted save. Will retry (fast-capped).");

        var deliveryBy = order.SelfManaged ? order.RequestedBy : vehicleName;

        // F3 — deliveryBy=null is a shape OMS accepts by design (pool-mode
        // dispatch sends it deliberately), so don't block; but a self-managed
        // order with no RequestedBy is a data gap at the source worth a trace.
        if (order.SelfManaged && string.IsNullOrWhiteSpace(deliveryBy))
        {
            _log.LogWarning(
                "[ShipmentStarted] Order {OrderId} is self-managed with no RequestedBy — sending deliveryBy=null (OMS accepts it; source system should supply RequestedBy)",
                order.Id);
        }

        var shipmentId = (await _trips.GetRootTripIdAsync(evt.TripId, ct)).ToString();
        // Business event time, not send time — stable across consumer retries
        // and outbox drains. Null trip is only reachable for self-managed
        // orders (the vendor path throws above); the event's OccurredOn was
        // stamped in the same code block as StartedAt, microseconds apart.
        var occurredAt = trip?.StartedAt ?? evt.OccurredOn;
        var context = new ShipmentStartedContext(shipmentId, order.OrderRef!, deliveryBy, occurredAt);
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
                "[ShipmentStarted] Fanned out {EventType} (order {OrderId}, trip {TripId}) to {N} subscriber(s)",
                eventType, order.Id, evt.TripId, subs.Count);
        }
        catch (DbUpdateException ex) when (CallbackFanout.IsUniqueViolation(ex))
        {
            _log.LogInformation(
                "[ShipmentStarted] Outbox rows for order={OrderId} trip={TripId} correlation={CorrelationId} already enqueued; skipping duplicate.",
                order.Id, evt.TripId, correlationId);
        }
    }
}
