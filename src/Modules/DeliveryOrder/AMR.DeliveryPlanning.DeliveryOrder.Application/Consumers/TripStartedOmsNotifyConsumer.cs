using System.Diagnostics;
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
    private readonly ILogger<TripStartedOmsNotifyConsumer> _logger;

    public TripStartedOmsNotifyConsumer(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        ILogger<TripStartedOmsNotifyConsumer> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
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
        var vendorVehicleKey = trip?.VendorVehicleKey;
        if (string.IsNullOrWhiteSpace(vendorVehicleKey))
        {
            // Throw instead of sending "(unknown)" — Option A semantics:
            // the OMS POST overwrites the deliveryBy field, so sending a
            // placeholder would clobber a previous-attempt's real vehicle.
            // MassTransit retry will re-read the trip; by then the racing
            // MarkVendorStarted save should have committed VendorVehicleKey.
            throw new InvalidOperationException(
                $"Trip {evt.TripId} has no VendorVehicleKey yet — race with TASK_PROCESSING save. Will retry.");
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
            DeliveryBy: vendorVehicleKey,
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
                "[OmsNotify] Trip {TripId} (attempt {N}) → OMS event=TripStarted outcome=Failed shipmentId={Sid} vehicle={VehKey} lots={LotCount} latencyMs={Ms}",
                evt.TripId, attemptNumber, shipmentId, vendorVehicleKey, lots.Count, sw.ElapsedMilliseconds);
            throw;
        }
        sw.Stop();

        await _auditRepository.AddAsync(new OrderAuditEvent(
            order.Id,
            AuditEventType,
            $"trip-started shipmentId={shipmentId} attempt={attemptNumber} vehicle={vendorVehicleKey} lots={lots.Count} latencyMs={sw.ElapsedMilliseconds}"),
            ct);
        await _auditRepository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[OmsNotify] Trip {TripId} (attempt {N}) → OMS event=TripStarted outcome=Success shipmentId={Sid} vehicle={VehKey} lots={LotCount} latencyMs={Ms}",
            evt.TripId, attemptNumber, shipmentId, vendorVehicleKey, lots.Count, sw.ElapsedMilliseconds);
    }
}
