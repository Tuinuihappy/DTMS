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
/// Phase OMS B4 — Notifies the upstream OMS that a shipment has reached
/// the terminal Failed state (vendor incident, robot crash, timeout
/// exhausted retries). Mirrors TripStartedOmsNotifyConsumer's gating +
/// audit semantics.
///
/// shipmentId resolves to the root Trip.Id (walking PreviousAttemptId)
/// so a failure on attempt N updates the same OMS shipment that
/// /shipments first registered on attempt 1.
/// </summary>
public class TripFailedOmsNotifyConsumer : IConsumer<TripFailedIntegrationEvent>
{
    private const string AuditEventType = "UpstreamOmsTripFailedNotified";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<TripFailedOmsNotifyConsumer> _logger;

    public TripFailedOmsNotifyConsumer(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<TripFailedOmsNotifyConsumer> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripFailedIntegrationEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        if (!_options.Enabled) return;
        if (evt.DeliveryOrderId == Guid.Empty) return;

        var order = await _orderRepository.GetByIdAsync(evt.DeliveryOrderId, ct);
        if (order is null || string.IsNullOrWhiteSpace(order.OrderRef)) return;

        // [Option A] Stable shipmentId across retry chain.
        var rootTripId = await _tripRepository.GetRootTripIdAsync(evt.TripId, ct);
        var shipmentId = rootTripId.ToString();
        var trip = await _tripRepository.GetByIdAsync(evt.TripId, ct);
        var attemptNumber = trip?.AttemptNumber ?? 1;

        // failureCategory is not on TripFailedIntegrationEvent today —
        // OMS will see "TripFailed" as a placeholder. If JobFacts'
        // structured FailureCategory becomes available on the event in
        // future, swap it in here.
        var payload = new OmsTripFailedNotification(
            FailureReason: evt.Reason,
            FailureCategory: "TripFailed",
            OccurredAt: evt.OccurredOn);

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentFailedAsync(shipmentId, payload, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[OmsFailed] Trip {TripId} (attempt {N}) → OMS event=TripFailed outcome=Failed shipmentId={Sid} latencyMs={Ms}",
                evt.TripId, attemptNumber, shipmentId, sw.ElapsedMilliseconds);
            throw;
        }
        sw.Stop();

        var auditDetails = $"trip-failed shipmentId={shipmentId} attempt={attemptNumber} reason=\"{Truncate(evt.Reason, 200)}\" latencyMs={sw.ElapsedMilliseconds}";
        await _auditRepository.AddAsync(new OrderAuditEvent(
            order.Id, AuditEventType, auditDetails), ct);
        await _auditRepository.SaveChangesAsync(ct);

        // P2.5 mirror to OrderActivity so OmsNotificationSection surfaces it.
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
            "[OmsFailed] Trip {TripId} (attempt {N}) → OMS event=TripFailed outcome=Success shipmentId={Sid} latencyMs={Ms}",
            evt.TripId, attemptNumber, shipmentId, sw.ElapsedMilliseconds);
    }

    private static string Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
