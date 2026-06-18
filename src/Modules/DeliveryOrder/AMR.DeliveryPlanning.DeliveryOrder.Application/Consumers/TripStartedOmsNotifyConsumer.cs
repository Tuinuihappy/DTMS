using System.Diagnostics;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.OmsAdapter.Infrastructure.Options;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Phase b/c — Notifies the upstream OMS that a shipment has started
/// once RIOT3 emits TASK_PROCESSING (Trip: Created → InProgress).
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
public class TripStartedOmsNotifyConsumer : IConsumer<TripStartedIntegrationEvent>
{
    private const string AuditEventType = "UpstreamOmsNotified";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<TripStartedOmsNotifyConsumer> _logger;

    public TripStartedOmsNotifyConsumer(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<TripStartedOmsNotifyConsumer> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripStartedIntegrationEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        if (!_options.Enabled)
        {
            _logger.LogDebug("[OmsNotify] disabled — skipping Trip {TripId}", evt.TripId);
            return;
        }

        if (evt.DeliveryOrderId == Guid.Empty)
        {
            _logger.LogDebug("[OmsNotify] Trip {TripId} has no DeliveryOrderId — skipping", evt.TripId);
            return;
        }

        var order = await _orderRepository.GetByIdAsync(evt.DeliveryOrderId, ct);
        if (order is null)
        {
            _logger.LogWarning("[OmsNotify] No DeliveryOrder for {OrderId} (Trip {TripId}) — skipping",
                evt.DeliveryOrderId, evt.TripId);
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

        var lots = order.Items
            .Where(i => i.TripId == evt.TripId)
            .Select(i => i.ItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (lots.Count == 0)
        {
            _logger.LogInformation(
                "[OmsNotify] Order {OrderId} Trip {TripId} has no bound items — pre-binding row, skipping",
                order.Id, evt.TripId);
            return;
        }

        var trip = await _tripRepository.GetByIdAsync(evt.TripId, ct);
        var vendorVehicleName = trip?.VendorVehicleName;
        if (string.IsNullOrWhiteSpace(vendorVehicleName))
        {
            // Throw instead of sending an empty/placeholder name — Option A
            // semantics: the OMS POST overwrites deliveryBy, so a blank
            // would clobber a previous-attempt's real vehicle. Name + Key
            // arrive together on RIOT3 TASK_PROCESSING and are captured
            // first-write-wins, so a missing Name here means the racing
            // MarkVendorStarted save hasn't committed yet — retry will
            // re-read once it has.
            throw new InvalidOperationException(
                $"Trip {evt.TripId} has no VendorVehicleName yet — race with TASK_PROCESSING save (Name + Key arrive together from RIOT3). Will retry.");
        }

        // [Option A] Stable shipmentId across retry chain. Walking
        // PreviousAttemptId back to the first attempt's Id means OMS sees
        // one shipment with vehicle/state updates per retry, not a fresh
        // shipment per attempt.
        var rootTripId = await _tripRepository.GetRootTripIdAsync(evt.TripId, ct);
        var shipmentId = rootTripId.ToString();
        var attemptNumber = trip!.AttemptNumber;

        var payload = new OmsShipmentNotification(
            ShipmentId: shipmentId,
            DeliveryBy: vendorVehicleName,
            Lots: lots.Select(id => new OmsLot(id)).ToList());

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentStartedAsync(payload, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[OmsNotify] Trip {TripId} (attempt {N}) → OMS event=TripStarted outcome=Failed shipmentId={Sid} vehicle={VehName} lots={LotCount} latencyMs={Ms}",
                evt.TripId, attemptNumber, shipmentId, vendorVehicleName, lots.Count, sw.ElapsedMilliseconds);
            throw;
        }
        sw.Stop();

        var auditDetails = $"trip-started shipmentId={shipmentId} attempt={attemptNumber} vehicle={vendorVehicleName} lots={lots.Count} latencyMs={sw.ElapsedMilliseconds}";
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
            relatedTripId: evt.TripId,
            attemptNumber: attemptNumber,
            cancellationToken: ct);

        _logger.LogInformation(
            "[OmsNotify] Trip {TripId} (attempt {N}) → OMS event=TripStarted outcome=Success shipmentId={Sid} vehicle={VehName} lots={LotCount} latencyMs={Ms}",
            evt.TripId, attemptNumber, shipmentId, vendorVehicleName, lots.Count, sw.ElapsedMilliseconds);
    }
}
