using System.Text;
using DTMS.Api.Infrastructure.Outbox;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.Api.Infrastructure.Callbacks;

/// <summary>
/// Phase S.5 (B2) — fans <see cref="TripDropCompletedIntegrationEvent"/> out as
/// <c>shipment.arrived.v1</c>, replacing the legacy
/// <c>TripDropCompletedOmsNotifyConsumer</c>. The OMS formatter keeps the legacy
/// <c>POST /api/shipments/{shipmentId}/arrived</c> contract (shipmentId in the
/// path, lots in the body).
///
/// <para>The legacy "one /arrived per shipment" audit-dedup is no longer
/// needed: P2 fire-once collapses duplicate drop events to one, the outbox
/// <c>(PartitionKey, CorrelationId)</c> index dedups consumer retries, and a
/// genuine cross-attempt duplicate now returns 409 which the dispatcher treats
/// as success.</para>
/// </summary>
public sealed class ShipmentArrivedCallbackFanoutConsumer
    : IConsumer<TripDropCompletedIntegrationEvent>
{
    private readonly ISubscriptionLookup _lookup;
    private readonly IServiceProvider _sp;
    private readonly OutboxDbContext _outbox;
    private readonly ITripRepository _trips;
    private readonly IDeliveryOrderRepository _orders;
    private readonly ShipmentCallbackOptions _options;
    private readonly ILogger<ShipmentArrivedCallbackFanoutConsumer> _log;

    public ShipmentArrivedCallbackFanoutConsumer(
        ISubscriptionLookup lookup,
        IServiceProvider sp,
        OutboxDbContext outbox,
        ITripRepository trips,
        IDeliveryOrderRepository orders,
        IOptions<ShipmentCallbackOptions> options,
        ILogger<ShipmentArrivedCallbackFanoutConsumer> log)
    {
        _lookup = lookup;
        _sp = sp;
        _outbox = outbox;
        _trips = trips;
        _orders = orders;
        _options = options.Value;
        _log = log;
    }

    public async Task Consume(ConsumeContext<TripDropCompletedIntegrationEvent> ctx)
    {
        if (!_options.ShipmentEventsEnabled) return;   // dark until cutover

        var ct = ctx.CancellationToken;
        var evt = ctx.Message;
        const string eventType = CallbackEventTypes.ShipmentArrivedV1;

        if (evt.DeliveryOrderId == Guid.Empty) return;

        var order = await _orders.GetByIdAsync(evt.DeliveryOrderId, ct);
        if (order is null || string.IsNullOrWhiteSpace(order.OrderRef))
            return;

        // Manual transport does not report arrival to OMS — the delivery is
        // handled outside DTMS's vendor pipeline (legacy skip).
        if (order.RequestedTransportMode == TransportMode.Manual)
            return;

        var source = order.SourceSystemKey;
        if (string.IsNullOrWhiteSpace(source)) return;

        var subs = (await _lookup.GetSubscribersAsync(eventType, ct))
            .Where(s => string.Equals(s.SystemKey, source, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (subs.Count == 0) return;

        var lots = order.Items
            .Where(i => i.TripId == evt.TripId)
            .Select(i => i.ItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        if (lots.Count == 0)
        {
            _log.LogInformation(
                "[ShipmentArrived] Order {OrderId} Trip {TripId} has no bound items — pre-binding row, skipping.",
                order.Id, evt.TripId);
            return;
        }

        var shipmentId = (await _trips.GetRootTripIdAsync(evt.TripId, ct)).ToString();
        var context = new OmsShipmentArrivedContext(shipmentId, lots);
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
                "[ShipmentArrived] Fanned out {EventType} (order {OrderId}, trip {TripId}) to {N} subscriber(s)",
                eventType, order.Id, evt.TripId, subs.Count);
        }
        catch (DbUpdateException ex) when (CallbackFanout.IsUniqueViolation(ex))
        {
            _log.LogInformation(
                "[ShipmentArrived] Outbox rows for order={OrderId} trip={TripId} correlation={CorrelationId} already enqueued; skipping duplicate.",
                order.Id, evt.TripId, correlationId);
        }
    }
}
