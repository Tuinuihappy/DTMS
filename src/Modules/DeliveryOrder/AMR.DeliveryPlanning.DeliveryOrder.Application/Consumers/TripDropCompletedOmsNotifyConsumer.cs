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
/// Notifies the upstream OMS that a shipment has arrived at the drop
/// station once RIOT3 emits SUB_TASK_FINISHED on the trip's drop. Fires
/// independently from the /started notification — if /started failed and
/// was never retried, /arrived will still go out and OMS will likely
/// reply 404 (unknown shipmentId), which surfaces via the Fault audit.
///
/// shipmentId is the root trip's Id (walking PreviousAttemptId back) so
/// /arrived for any retry attempt updates the same OMS shipment that
/// /shipments registered on attempt 1.
/// </summary>
public class TripDropCompletedOmsNotifyConsumer : IConsumer<TripDropCompletedIntegrationEvent>
{
    private const string AuditEventType = "UpstreamOmsArrivedNotified";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly ILogger<TripDropCompletedOmsNotifyConsumer> _logger;

    public TripDropCompletedOmsNotifyConsumer(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        ILogger<TripDropCompletedOmsNotifyConsumer> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripDropCompletedIntegrationEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        if (!_options.Enabled)
        {
            _logger.LogDebug("[OmsArrived] disabled — skipping Trip {TripId}", evt.TripId);
            return;
        }

        if (evt.DeliveryOrderId == Guid.Empty)
        {
            _logger.LogDebug("[OmsArrived] Trip {TripId} has no DeliveryOrderId — skipping", evt.TripId);
            return;
        }

        var order = await _orderRepository.GetByIdAsync(evt.DeliveryOrderId, ct);
        if (order is null)
        {
            _logger.LogWarning("[OmsArrived] No DeliveryOrder for {OrderId} (Trip {TripId}) — skipping",
                evt.DeliveryOrderId, evt.TripId);
            return;
        }

        if (string.IsNullOrWhiteSpace(order.OrderRef))
        {
            _logger.LogDebug("[OmsArrived] Order {OrderId} has no OrderRef — non-upstream, skipping",
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
                "[OmsArrived] Order {OrderId} Trip {TripId} has no bound items — pre-binding row, skipping",
                order.Id, evt.TripId);
            return;
        }

        // [Option A] shipmentId stable across retry chain.
        var rootTripId = await _tripRepository.GetRootTripIdAsync(evt.TripId, ct);
        var shipmentId = rootTripId.ToString();
        // Best-effort attempt number for the audit row. Pre-binding rows
        // or chain breaks fall through with attempt=1 — harmless.
        var trip = await _tripRepository.GetByIdAsync(evt.TripId, ct);
        var attemptNumber = trip?.AttemptNumber ?? 1;

        var lotPayload = lots.Select(id => new OmsLot(id)).ToList();

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentArrivedAsync(shipmentId, lotPayload, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[OmsArrived] Trip {TripId} (attempt {N}) → OMS event=TripDropCompleted outcome=Failed shipmentId={Sid} lots={LotCount} latencyMs={Ms}",
                evt.TripId, attemptNumber, shipmentId, lots.Count, sw.ElapsedMilliseconds);
            throw;
        }
        sw.Stop();

        await _auditRepository.AddAsync(new OrderAuditEvent(
            order.Id,
            AuditEventType,
            $"trip-arrived shipmentId={shipmentId} attempt={attemptNumber} lots={lots.Count} latencyMs={sw.ElapsedMilliseconds}"),
            ct);
        await _auditRepository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[OmsArrived] Trip {TripId} (attempt {N}) → OMS event=TripDropCompleted outcome=Success shipmentId={Sid} lots={LotCount} latencyMs={Ms}",
            evt.TripId, attemptNumber, shipmentId, lots.Count, sw.ElapsedMilliseconds);
    }
}
