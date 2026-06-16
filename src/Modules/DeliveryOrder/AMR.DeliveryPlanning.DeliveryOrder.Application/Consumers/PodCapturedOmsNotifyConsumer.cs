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
/// Phase OMS B4 — Notifies the upstream OMS that POD scan completed.
/// Separate stage from /arrived: arrived = robot physically reached the
/// drop; pod-completed = customer/operator confirmed receipt via scan.
///
/// PodCapturedIntegrationEvent only carries TripId + StopId + ScannedIds
/// — DeliveryOrderId is resolved via Trip lookup.
/// </summary>
public class PodCapturedOmsNotifyConsumer : IConsumer<PodCapturedIntegrationEvent>
{
    private const string AuditEventType = "UpstreamOmsPodCompletedNotified";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<PodCapturedOmsNotifyConsumer> _logger;

    public PodCapturedOmsNotifyConsumer(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<PodCapturedOmsNotifyConsumer> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PodCapturedIntegrationEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        if (!_options.Enabled) return;
        if (evt.TripId == Guid.Empty) return;
        if (evt.ScannedIds is null || evt.ScannedIds.Count == 0) return;

        // PodCaptured event lacks DeliveryOrderId — resolve via Trip.
        var trip = await _tripRepository.GetByIdAsync(evt.TripId, ct);
        if (trip is null || trip.DeliveryOrderId == Guid.Empty) return;

        var order = await _orderRepository.GetByIdAsync(trip.DeliveryOrderId, ct);
        if (order is null || string.IsNullOrWhiteSpace(order.OrderRef)) return;

        var rootTripId = await _tripRepository.GetRootTripIdAsync(evt.TripId, ct);
        var shipmentId = rootTripId.ToString();
        var attemptNumber = trip.AttemptNumber;

        var payload = new OmsPodCompletedNotification(
            ScannedIds: evt.ScannedIds,
            ScannedAt: evt.OccurredOn);

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentPodCompletedAsync(shipmentId, payload, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[OmsPod] Trip {TripId} (attempt {N}) → OMS event=PodCompleted outcome=Failed shipmentId={Sid} scanned={ScannedCount} latencyMs={Ms}",
                evt.TripId, attemptNumber, shipmentId, evt.ScannedIds.Count, sw.ElapsedMilliseconds);
            throw;
        }
        sw.Stop();

        var auditDetails = $"pod-completed shipmentId={shipmentId} attempt={attemptNumber} scanned={evt.ScannedIds.Count} latencyMs={sw.ElapsedMilliseconds}";
        await _auditRepository.AddAsync(new OrderAuditEvent(
            order.Id, AuditEventType, auditDetails), ct);
        await _auditRepository.SaveChangesAsync(ct);

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
            "[OmsPod] Trip {TripId} (attempt {N}) → OMS event=PodCompleted outcome=Success shipmentId={Sid} scanned={ScannedCount} latencyMs={Ms}",
            evt.TripId, attemptNumber, shipmentId, evt.ScannedIds.Count, sw.ElapsedMilliseconds);
    }
}
