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
/// Phase OMS B4 — Notifies the upstream OMS that a shipment was
/// operator-cancelled. Distinct from TripFailedOmsNotifyConsumer (system
/// incident): cancellation is intentional, receiver typically marks
/// terminal-no-retry on its side.
/// </summary>
public class TripCancelledOmsNotifyConsumer : IConsumer<TripCancelledIntegrationEvent>
{
    private const string AuditEventType = "UpstreamOmsTripCancelledNotified";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<TripCancelledOmsNotifyConsumer> _logger;

    public TripCancelledOmsNotifyConsumer(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<TripCancelledOmsNotifyConsumer> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripCancelledIntegrationEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        if (!_options.Enabled) return;
        if (evt.DeliveryOrderId == Guid.Empty) return;

        var order = await _orderRepository.GetByIdAsync(evt.DeliveryOrderId, ct);
        if (order is null || string.IsNullOrWhiteSpace(order.OrderRef)) return;

        var rootTripId = await _tripRepository.GetRootTripIdAsync(evt.TripId, ct);
        var shipmentId = rootTripId.ToString();
        var trip = await _tripRepository.GetByIdAsync(evt.TripId, ct);
        var attemptNumber = trip?.AttemptNumber ?? 1;

        var payload = new OmsTripCancelledNotification(
            CancelReason: evt.Reason,
            CancelledBy: evt.TriggeredBy,
            OccurredAt: evt.OccurredOn);

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentCancelledAsync(shipmentId, payload, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[OmsCancelled] Trip {TripId} (attempt {N}) → OMS event=TripCancelled outcome=Failed shipmentId={Sid} latencyMs={Ms}",
                evt.TripId, attemptNumber, shipmentId, sw.ElapsedMilliseconds);
            throw;
        }
        sw.Stop();

        var auditDetails = $"trip-cancelled shipmentId={shipmentId} attempt={attemptNumber} reason=\"{Truncate(evt.Reason, 200)}\" by={evt.TriggeredBy ?? "(unknown)"} latencyMs={sw.ElapsedMilliseconds}";
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
            actorId: evt.TriggeredBy,
            occurredAt: DateTime.UtcNow,
            relatedTripId: evt.TripId,
            attemptNumber: attemptNumber,
            cancellationToken: ct);

        _logger.LogInformation(
            "[OmsCancelled] Trip {TripId} (attempt {N}) → OMS event=TripCancelled outcome=Success shipmentId={Sid} latencyMs={Ms}",
            evt.TripId, attemptNumber, shipmentId, sw.ElapsedMilliseconds);
    }

    private static string Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
