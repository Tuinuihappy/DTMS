using System.Diagnostics;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.OmsAdapter.Abstractions;
using DTMS.OmsAdapter.Abstractions.Models;
using DTMS.OmsAdapter.Infrastructure.Options;
// Phase S.6 follow-up — outbound URL+token resolved at call time via
// IOmsCallbackTargetResolver (UI-driven SystemCredentials, env fallback).
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.DeliveryOrder.Application.Consumers;

/// <summary>
/// Phase b/c — Notifies the upstream OMS that a shipment has started
/// once RIOT3 emits TASK_PROCESSING (Trip: Created → InProgress).
///
/// WMS PR-4b — also consumes <c>TripDispatchedIntegrationEventV1</c>
/// so Manual/Fleet pool trips notify OMS at dispatch time (before any
/// operator claims). In that path <c>DeliveryBy</c> is null — the shared
/// consume method routes on the presence of a vehicle name to keep the
/// two flows in a single place.
///
/// Gated by:
///   • UpstreamOms:Enabled kill switch (dev/test).
///   • Order.OrderRef presence — only upstream-originated orders get
///     notified; manual/draft orders aren't known to OMS.
///   • Items bound to this Trip (Item.TripId == evt.TripId). Pre-binding
///     rows degrade silently.
///
/// On HTTP failure, the client throws; MassTransit retry policy + the
/// paired Fault consumer (TripStartedOmsNotifyFaultConsumer) handle the
/// dead-letter audit.
/// </summary>
public class TripStartedOmsNotifyConsumer :
    IConsumer<TripStartedIntegrationEvent>,
    IConsumer<TripDispatchedIntegrationEventV1>
{
    private const string AuditEventType = "UpstreamOmsNotified";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly IOmsCallbackTargetResolver _targetResolver;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<TripStartedOmsNotifyConsumer> _logger;

    public TripStartedOmsNotifyConsumer(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        IOmsCallbackTargetResolver targetResolver,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<TripStartedOmsNotifyConsumer> logger)
    {
        _options = options.Value;
        _client = client;
        _targetResolver = targetResolver;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TripStartedIntegrationEvent> context) =>
        NotifyAsync(
            eventType: "TripStarted",
            tripId: context.Message.TripId,
            deliveryOrderId: context.Message.DeliveryOrderId,
            requireVendorVehicleName: true,
            ct: context.CancellationToken);

    // WMS PR-4b — pool-dispatch notification. Fires immediately at
    // dispatch time; no vehicle/operator claimed yet so DeliveryBy = null.
    public Task Consume(ConsumeContext<TripDispatchedIntegrationEventV1> context) =>
        NotifyAsync(
            eventType: "TripDispatched",
            tripId: context.Message.TripId,
            deliveryOrderId: context.Message.DeliveryOrderId,
            requireVendorVehicleName: false,
            ct: context.CancellationToken);

    private async Task NotifyAsync(
        string eventType, Guid tripId, Guid deliveryOrderId,
        bool requireVendorVehicleName, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[OmsNotify] disabled — skipping Trip {TripId}", tripId);
            return;
        }

        if (deliveryOrderId == Guid.Empty)
        {
            _logger.LogDebug("[OmsNotify] Trip {TripId} has no DeliveryOrderId — skipping", tripId);
            return;
        }

        var order = await _orderRepository.GetByIdAsync(deliveryOrderId, ct);
        if (order is null)
        {
            _logger.LogWarning("[OmsNotify] No DeliveryOrder for {OrderId} (Trip {TripId}) — skipping",
                deliveryOrderId, tripId);
            return;
        }

        // OrderRef is the canonical upstream external ref. Empty = locally
        // created (draft / manual) — OMS doesn't know this shipment, so
        // notifying would 4xx and just churn the retry queue.
        if (string.IsNullOrWhiteSpace(order.OrderRef))
        {
            _logger.LogDebug("[OmsNotify] Order {OrderId} has no OrderRef — non-upstream, skipping",
                order.Id);
            return;
        }

        // S.3.1b-followup guard — only OMS-sourced orders go through this
        // legacy adapter. Sap/Erp orders carry an OrderRef too, so the
        // gate above alone would leak them into the OMS POST. The
        // federated S.3.1b pipeline handles non-OMS sources via the
        // SystemEventSubscriptions table; legacy stays OMS-only by
        // design.
        if (!string.Equals(order.SourceSystemKey, WellKnownSourceSystems.Oms, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "[OmsNotify] Order {OrderId} source={Source} — not OMS, skipping legacy adapter (S.3.1b handles routing)",
                order.Id, order.SourceSystemKey);
            return;
        }

        var lots = order.Items
            .Where(i => i.TripId == tripId)
            .Select(i => i.ItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (lots.Count == 0)
        {
            // Pool dispatch may see the event before AssignItemsToTripCommand
            // has committed the item→trip binding. Throwing lets MassTransit
            // retry (backoff up to 3 attempts) so we don't lose the OMS
            // notification just because the two saves raced. AMR path used
            // to log-and-skip here — same behaviour for the started event
            // preserves legacy semantics.
            if (eventType == "TripDispatched")
                throw new InvalidOperationException(
                    $"Trip {tripId} has no bound items yet — race with AssignItemsToTripCommand save. Will retry.");
            _logger.LogInformation(
                "[OmsNotify] Order {OrderId} Trip {TripId} has no bound items — pre-binding row, skipping",
                order.Id, tripId);
            return;
        }

        var trip = await _tripRepository.GetByIdAsync(tripId, ct);

        // WMS PR-4b — Pool trips are notified ONCE at dispatch time via
        // TripDispatchedIntegrationEventV1 (DeliveryBy=null). When the
        // operator subsequently claims, TripStartedIntegrationEvent also
        // reaches this consumer, but re-POSTing to OMS would duplicate the
        // shipment notify for the same shipmentId. Trip.DispatchedAt is the
        // pool signature: AMR trips never dispatch to a pool (it stays null),
        // Manual/Fleet pool trips always have it stamped. Skip the second
        // notify for pool trips on TripStarted.
        if (eventType == "TripStarted" && trip?.DispatchedAt is not null)
        {
            _logger.LogInformation(
                "[OmsNotify] Trip {TripId} — pool trip already notified at dispatch time (DispatchedAt={DispatchedAt}); skipping duplicate TripStarted POST.",
                tripId, trip.DispatchedAt);
            return;
        }

        // Self-managed orders (source system executes the transport itself)
        // have no vendor vehicle — the auto TripStarted would otherwise wait
        // forever for a RIOT3 vehicle name that never arrives. Treat them like
        // the pool path: notify OMS once with DeliveryBy=null.
        var requireName = requireVendorVehicleName && !order.SelfManaged;

        var vendorVehicleName = trip?.VendorVehicleName;
        if (requireName && string.IsNullOrWhiteSpace(vendorVehicleName))
        {
            // Throw instead of sending an empty/placeholder name — Option A
            // semantics: the OMS POST overwrites deliveryBy, so a blank
            // would clobber a previous-attempt's real vehicle. Name + Key
            // arrive together on RIOT3 TASK_PROCESSING and are captured
            // first-write-wins, so a missing Name here means the racing
            // MarkVendorStarted save hasn't committed yet — retry will
            // re-read once it has.
            throw new InvalidOperationException(
                $"Trip {tripId} has no VendorVehicleName yet — race with TASK_PROCESSING save (Name + Key arrive together from RIOT3). Will retry.");
        }
        // DeliveryBy semantics per flow:
        //   • AMR   → the vendor vehicle name (robot) reported by RIOT3.
        //   • Pool  → null on TripDispatched ("vehicle not yet known").
        //   • Self-managed → the order's RequestedBy: the external actor is
        //     the closest thing to "who is delivering" since the source system
        //     runs the transport itself (no vendor vehicle). RequestedBy is
        //     required on self-managed orders, so it's present here.
        string? deliveryBy = order.SelfManaged
            ? order.RequestedBy
            : (requireName ? vendorVehicleName : null);

        // [Option A] Stable shipmentId across retry chain. Walking
        // PreviousAttemptId back to the first attempt's Id means OMS sees
        // one shipment with vehicle/state updates per retry, not a fresh
        // shipment per attempt.
        var rootTripId = await _tripRepository.GetRootTripIdAsync(tripId, ct);
        var shipmentId = rootTripId.ToString();
        var attemptNumber = trip!.AttemptNumber;

        var payload = new OmsShipmentNotification(
            ShipmentId: shipmentId,
            DeliveryBy: deliveryBy,
            Lots: lots.Select(id => new OmsLot(id)).ToList());

        var target = await _targetResolver.ResolveAsync("oms", ct);
        if (target is null)
        {
            _logger.LogInformation(
                "[OmsNotify] No callback target resolved for 'oms' (neither SystemCredentials.CallbackBaseUrl nor UpstreamOms__BaseUrl set) — skipping Trip {TripId}",
                tripId);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentStartedAsync(target, payload, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[OmsNotify] Trip {TripId} (attempt {N}) → OMS event={EventType} outcome=Failed shipmentId={Sid} vehicle={VehName} lots={LotCount} latencyMs={Ms}",
                tripId, attemptNumber, eventType, shipmentId, deliveryBy, lots.Count, sw.ElapsedMilliseconds);
            throw;
        }
        sw.Stop();

        var auditDetails = $"{eventType.ToLowerInvariant()} shipmentId={shipmentId} attempt={attemptNumber} vehicle={deliveryBy ?? "(none)"} lots={lots.Count} latencyMs={sw.ElapsedMilliseconds}";
        await _auditRepository.AddAsync(new OrderAuditEvent(
            order.Id, AuditEventType, auditDetails), ct);
        await _auditRepository.SaveChangesAsync(ct);

        // P2.5 mirror: OmsNotify outcomes don't flow through an integration
        // event, so the OrderActivity projector can't pick them up. Mirror
        // into the unified audit timeline so the OmsNotificationSection UI
        // (which reads OrderActivity) reflects the notification.
        await _activityStore.AppendAsync(
            projectorName: "OmsNotifyDirect",
            eventId: Guid.NewGuid(),
            orderId: order.Id,
            category: "OmsNotify",
            eventType: AuditEventType,
            details: auditDetails,
            actorId: null,
            occurredAt: DateTime.UtcNow,
            relatedTripId: tripId,
            attemptNumber: attemptNumber,
            cancellationToken: ct);

        _logger.LogInformation(
            "[OmsNotify] Trip {TripId} (attempt {N}) → OMS event={EventType} outcome=Success shipmentId={Sid} vehicle={VehName} lots={LotCount} latencyMs={Ms}",
            tripId, attemptNumber, eventType, shipmentId, deliveryBy, lots.Count, sw.ElapsedMilliseconds);
    }
}
