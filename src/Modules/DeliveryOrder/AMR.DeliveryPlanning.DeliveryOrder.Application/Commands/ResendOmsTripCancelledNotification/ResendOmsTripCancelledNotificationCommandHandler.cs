using System.Diagnostics;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.OmsAdapter.Infrastructure.Options;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ResendOmsTripCancelledNotification;

public class ResendOmsTripCancelledNotificationCommandHandler
    : ICommandHandler<ResendOmsTripCancelledNotificationCommand, ResendOmsTripCancelledNotificationResult>
{
    private const string AuditEventType = "UpstreamOmsTripCancelledManuallyResent";
    private const string ResendReason = "operator-requested resend";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<ResendOmsTripCancelledNotificationCommandHandler> _logger;

    public ResendOmsTripCancelledNotificationCommandHandler(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<ResendOmsTripCancelledNotificationCommandHandler> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task<Result<ResendOmsTripCancelledNotificationResult>> Handle(
        ResendOmsTripCancelledNotificationCommand request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return Result<ResendOmsTripCancelledNotificationResult>.Failure(
                "Upstream OMS notifications are disabled. Toggle UpstreamOms:Enabled to resend.");

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ResendOmsTripCancelledNotificationResult>.Failure($"Order {request.OrderId} not found.");
        if (string.IsNullOrWhiteSpace(order.OrderRef))
            return Result<ResendOmsTripCancelledNotificationResult>.Failure(
                "Order has no OrderRef — only upstream-originated orders can be resent to OMS.");

        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<ResendOmsTripCancelledNotificationResult>.Failure($"Trip {request.TripId} not found.");
        if (trip.DeliveryOrderId != request.OrderId)
            return Result<ResendOmsTripCancelledNotificationResult>.Failure(
                $"Trip {request.TripId} does not belong to order {request.OrderId}.");
        if (trip.Status != TripStatus.Cancelled)
            return Result<ResendOmsTripCancelledNotificationResult>.Failure(
                $"Trip {request.TripId} is not in Cancelled state (current: {trip.Status}). Cannot resend cancellation notification.");

        var rootTripId = await _tripRepository.GetRootTripIdAsync(trip.Id, cancellationToken);
        var shipmentId = rootTripId.ToString();
        var payload = new OmsTripCancelledNotification(
            CancelReason: ResendReason,
            CancelledBy: request.RequestedBy,
            OccurredAt: trip.CompletedAt ?? DateTime.UtcNow);

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentCancelledAsync(shipmentId, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[OmsCancelledResend] Trip {TripId} manual resend failed: {Error}",
                trip.Id, ex.Message);
            return Result<ResendOmsTripCancelledNotificationResult>.Failure(
                $"OMS request failed: {ex.Message}");
        }
        sw.Stop();

        var auditDetails = $"trip-cancelled shipmentId={shipmentId} attempt={trip.AttemptNumber} reason=\"{ResendReason}\" by={request.RequestedBy ?? "(anonymous)"} latencyMs={sw.ElapsedMilliseconds}";
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
            "[OmsCancelledResend] Trip {TripId} (attempt {N}) → OMS event=ManualCancelledResend outcome=Success shipmentId={Sid} latencyMs={Ms} by={By}",
            trip.Id, trip.AttemptNumber, shipmentId, sw.ElapsedMilliseconds,
            request.RequestedBy ?? "(anonymous)");

        return Result<ResendOmsTripCancelledNotificationResult>.Success(new ResendOmsTripCancelledNotificationResult(
            ShipmentId: shipmentId, LatencyMs: sw.ElapsedMilliseconds));
    }
}
