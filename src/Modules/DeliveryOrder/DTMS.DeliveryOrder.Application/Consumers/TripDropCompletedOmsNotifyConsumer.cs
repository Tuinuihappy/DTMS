using System.Diagnostics;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.OmsAdapter.Abstractions;
using DTMS.OmsAdapter.Abstractions.Models;
using DTMS.OmsAdapter.Infrastructure.Options;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.DeliveryOrder.Application.Consumers;

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
    private readonly IOmsCallbackTargetResolver _targetResolver;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<TripDropCompletedOmsNotifyConsumer> _logger;

    public TripDropCompletedOmsNotifyConsumer(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        IOmsCallbackTargetResolver targetResolver,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<TripDropCompletedOmsNotifyConsumer> logger)
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

        // S.3.1b-followup guard — see TripStartedOmsNotifyConsumer for
        // the full rationale. Legacy adapter handles OMS only; Sap/Erp
        // route through the S.3.1b SystemEventSubscriptions pipeline.
        if (order.SourceSystem != SourceSystem.Oms)
        {
            _logger.LogDebug(
                "[OmsArrived] Order {OrderId} source={Source} — not OMS, skipping legacy adapter",
                order.Id, order.SourceSystem);
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

        var target = await _targetResolver.ResolveAsync("oms", ct);
        if (target is null)
        {
            _logger.LogInformation(
                "[OmsArrived] No callback target resolved for 'oms' (neither SystemCredentials.CallbackBaseUrl nor UpstreamOms__BaseUrl set) — skipping Trip {TripId}",
                evt.TripId);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentArrivedAsync(target, shipmentId, lotPayload, ct);
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

        var auditDetails = $"trip-arrived shipmentId={shipmentId} attempt={attemptNumber} lots={lots.Count} latencyMs={sw.ElapsedMilliseconds}";
        await _auditRepository.AddAsync(new OrderAuditEvent(
            order.Id, AuditEventType, auditDetails), ct);
        await _auditRepository.SaveChangesAsync(ct);

        // P2.5 mirror: see TripStartedOmsNotifyConsumer for rationale.
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
            "[OmsArrived] Trip {TripId} (attempt {N}) → OMS event=TripDropCompleted outcome=Success shipmentId={Sid} lots={LotCount} latencyMs={Ms}",
            evt.TripId, attemptNumber, shipmentId, lots.Count, sw.ElapsedMilliseconds);
    }
}
