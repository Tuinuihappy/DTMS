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
/// Fans <see cref="TripCancelledIntegrationEvent"/> out to the order's source
/// system as <c>shipment.cancelled.v1</c>. Since 0f123c2 nothing told upstream a
/// shipment had died, so a cancelled shipment stayed "in progress" there
/// forever. The order-scoped fan-out cannot fill the gap: it cannot address an
/// OMS shipment, because one order spans N root trips.
///
/// <para>This is the federated re-do of the deleted TripCancelledOmsNotifyConsumer,
/// same event and same root-trip-id contract — 0f123c2 removed it because OMS had
/// dropped <c>/api/shipments/{id}/cancelled</c>, not because the design was wrong.
/// Why OMS dropped it is unresolved, so the subscription ships disabled; see
/// docs/oms-shipment-cancel-contract.md before enabling.</para>
///
/// <para>Trip-scoped like its started/arrived siblings, which is what makes the
/// id work: shipmentId = root trip id, the same token the subscriber already
/// received from <c>shipment.started.v1</c>. An order cancellation cascades into
/// one trip cancellation per active trip, so it fans out as one callback per
/// shipment — distinct shipments, not duplicates.</para>
///
/// <para>Guards are stricter than the deleted consumer, which had none of the
/// trip-state ones and cancelled unconditionally: pool trips and never-started
/// trips are skipped here because no <c>started</c> was sent for either, so the
/// subscriber has never heard of those shipments.</para>
///
/// <para>Not terminal: a retry reuses the root trip id, so a subscriber can see
/// started(X) → cancelled(X) → started(X). Retries are operator-driven and land
/// seconds to minutes after the cancel, so "no retry will follow" is unknowable
/// here. Subscribers must tolerate the resurrection and must be idempotent on
/// cancel — a retried chain that ultimately dies sends cancel(X) once per
/// attempt, each under its own CorrelationId, which the outbox's uniqueness
/// index does not collapse.</para>
/// </summary>
public sealed class ShipmentCancelledCallbackFanoutConsumer
    : IConsumer<TripCancelledIntegrationEvent>
{
    private readonly ISubscriptionLookup _lookup;
    private readonly IServiceProvider _sp;
    private readonly OutboxDbContext _outbox;
    private readonly ITripRepository _trips;
    private readonly IDeliveryOrderRepository _orders;
    private readonly ILogger<ShipmentCancelledCallbackFanoutConsumer> _log;

    public ShipmentCancelledCallbackFanoutConsumer(
        ISubscriptionLookup lookup,
        IServiceProvider sp,
        OutboxDbContext outbox,
        ITripRepository trips,
        IDeliveryOrderRepository orders,
        ILogger<ShipmentCancelledCallbackFanoutConsumer> log)
    {
        _lookup = lookup;
        _sp = sp;
        _outbox = outbox;
        _trips = trips;
        _orders = orders;
        _log = log;
    }

    public async Task Consume(ConsumeContext<TripCancelledIntegrationEvent> ctx)
    {
        var ct = ctx.CancellationToken;
        var evt = ctx.Message;
        const string eventType = CallbackEventTypes.ShipmentCancelledV1;

        if (evt.DeliveryOrderId == Guid.Empty) return;

        // TripCancelledIntegrationEvent carries no SourceSystem (unlike the
        // order events), so the order lookup is what routes this at all.
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
        if (trip is null)
        {
            // Fail closed: without the trip we cannot resolve the root id, and
            // guessing would cancel the wrong shipment.
            _log.LogWarning(
                "[ShipmentCancelled] Trip {TripId} not found for order {OrderId}; skipping.",
                evt.TripId, order.Id);
            return;
        }

        // Only cancel shipments the subscriber was actually told about.
        //
        // Pool trips: ShipmentStarted skips these too. Its comment claims they
        // were "already notified at dispatch", but the consumer that did that
        // (TripStartedOmsNotifyConsumer) is deleted and nothing replaced it —
        // so no started is sent for them at all, and a cancel would name a
        // shipment the subscriber has never seen.
        if (trip.DispatchedAt is not null)
        {
            _log.LogInformation(
                "[ShipmentCancelled] Trip {TripId} is a pool trip — no started was ever sent; skipping.",
                evt.TripId);
            return;
        }

        // StartedAt is set on the one path that raises TripStarted, so a null
        // here means no shipment.started ever went out — e.g. a Created trip
        // killed by the order-cancelled cascade.
        if (trip.StartedAt is null)
        {
            _log.LogInformation(
                "[ShipmentCancelled] Trip {TripId} never started — nothing to cancel upstream; skipping.",
                evt.TripId);
            return;
        }

        // No lot lookup on purpose — see ShipmentCancelledContext. TripCancelledConsumer
        // unbinds this trip's items while handling the same event on its own queue.

        var shipmentId = (await _trips.GetRootTripIdAsync(evt.TripId, ct)).ToString();
        var context = new ShipmentCancelledContext(
            shipmentId, evt.Reason, evt.TriggeredBy, evt.OccurredOn);

        // Deterministic per (event type, trip): a single cancel action arrives
        // as TWO integration events — the operator command and RIOT3's
        // TASK_CANCELED echo (~400ms later, reason "vendor cancelled"), which
        // races past Trip.Cancel's already-cancelled guard. Verified live
        // 2026-07-22: both fanned out, so the subscriber got the same shipment
        // cancelled twice with conflicting reasons. Keying correlation on the
        // TripId (not ctx.MessageId) makes the second insert hit the
        // (PartitionKey, CorrelationId) unique index → caught below as the
        // idempotent no-op. First event wins, which also picks the true
        // cancel reason — the echo's is a placeholder. A retried trip gets a
        // new TripId, so per-attempt cancels stay distinct as documented above.
        var correlationId = CallbackFanout.DeterministicCorrelationId(
            $"{eventType}:{evt.TripId}");

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
                "[ShipmentCancelled] Fanned out {EventType} (order {OrderId}, trip {TripId}, shipment {ShipmentId}) to {N} subscriber(s)",
                eventType, order.Id, evt.TripId, shipmentId, subs.Count);
        }
        catch (DbUpdateException ex) when (CallbackFanout.IsUniqueViolation(ex))
        {
            _log.LogInformation(
                "[ShipmentCancelled] Outbox rows for order={OrderId} trip={TripId} correlation={CorrelationId} already enqueued; skipping duplicate.",
                order.Id, evt.TripId, correlationId);
        }
    }
}
