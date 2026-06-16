using System.Diagnostics;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.OmsAdapter.Infrastructure.Options;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ResendOmsPodCompletedNotification;

public class ResendOmsPodCompletedNotificationCommandHandler
    : ICommandHandler<ResendOmsPodCompletedNotificationCommand, ResendOmsPodCompletedNotificationResult>
{
    private const string AuditEventType = "UpstreamOmsPodCompletedManuallyResent";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<ResendOmsPodCompletedNotificationCommandHandler> _logger;

    public ResendOmsPodCompletedNotificationCommandHandler(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<ResendOmsPodCompletedNotificationCommandHandler> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task<Result<ResendOmsPodCompletedNotificationResult>> Handle(
        ResendOmsPodCompletedNotificationCommand request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return Result<ResendOmsPodCompletedNotificationResult>.Failure(
                "Upstream OMS notifications are disabled. Toggle UpstreamOms:Enabled to resend.");

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ResendOmsPodCompletedNotificationResult>.Failure($"Order {request.OrderId} not found.");
        if (string.IsNullOrWhiteSpace(order.OrderRef))
            return Result<ResendOmsPodCompletedNotificationResult>.Failure(
                "Order has no OrderRef — only upstream-originated orders can be resent to OMS.");

        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<ResendOmsPodCompletedNotificationResult>.Failure($"Trip {request.TripId} not found.");
        if (trip.DeliveryOrderId != request.OrderId)
            return Result<ResendOmsPodCompletedNotificationResult>.Failure(
                $"Trip {request.TripId} does not belong to order {request.OrderId}.");

        var scannedIds = order.Items
            .Where(i => i.TripId == request.TripId)
            .Select(i => i.ItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (scannedIds.Count == 0)
            return Result<ResendOmsPodCompletedNotificationResult>.Failure(
                "No items are bound to this trip — nothing to send.");

        var rootTripId = await _tripRepository.GetRootTripIdAsync(trip.Id, cancellationToken);
        var shipmentId = rootTripId.ToString();
        var payload = new OmsPodCompletedNotification(
            ScannedIds: scannedIds,
            ScannedAt: trip.CompletedAt ?? DateTime.UtcNow);

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentPodCompletedAsync(shipmentId, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[OmsPodResend] Trip {TripId} manual resend failed: {Error}",
                trip.Id, ex.Message);
            return Result<ResendOmsPodCompletedNotificationResult>.Failure(
                $"OMS request failed: {ex.Message}");
        }
        sw.Stop();

        var auditDetails = $"pod-completed shipmentId={shipmentId} attempt={trip.AttemptNumber} scanned={scannedIds.Count} latencyMs={sw.ElapsedMilliseconds}";
        await _auditRepository.AddAsync(new OrderAuditEvent(
            order.Id, AuditEventType, auditDetails, actorId: request.RequestedBy),
            cancellationToken);
        await _auditRepository.SaveChangesAsync(cancellationToken);

        await _activityStore.AppendAsync(
            projectorName: "OmsNotifyDirect",
            eventId: Guid.NewGuid(),
            orderId: order.Id,
            category: "OmsNotify",
            eventType: AuditEventType,
            details: auditDetails,
            actorId: request.RequestedBy,
            occurredAt: DateTime.UtcNow,
            relatedTripId: trip.Id,
            attemptNumber: trip.AttemptNumber,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "[OmsPodResend] Trip {TripId} (attempt {N}) → OMS event=ManualPodResend outcome=Success shipmentId={Sid} scanned={ScannedCount} latencyMs={Ms} by={By}",
            trip.Id, trip.AttemptNumber, shipmentId, scannedIds.Count, sw.ElapsedMilliseconds,
            request.RequestedBy ?? "(anonymous)");

        return Result<ResendOmsPodCompletedNotificationResult>.Success(new ResendOmsPodCompletedNotificationResult(
            ShipmentId: shipmentId, ScannedCount: scannedIds.Count, LatencyMs: sw.ElapsedMilliseconds));
    }
}
