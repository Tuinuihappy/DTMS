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

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ResendOmsTripFailedNotification;

public class ResendOmsTripFailedNotificationCommandHandler
    : ICommandHandler<ResendOmsTripFailedNotificationCommand, ResendOmsTripFailedNotificationResult>
{
    private const string AuditEventType = "UpstreamOmsTripFailedManuallyResent";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<ResendOmsTripFailedNotificationCommandHandler> _logger;

    public ResendOmsTripFailedNotificationCommandHandler(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<ResendOmsTripFailedNotificationCommandHandler> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task<Result<ResendOmsTripFailedNotificationResult>> Handle(
        ResendOmsTripFailedNotificationCommand request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return Result<ResendOmsTripFailedNotificationResult>.Failure(
                "Upstream OMS notifications are disabled. Toggle UpstreamOms:Enabled to resend.");

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ResendOmsTripFailedNotificationResult>.Failure($"Order {request.OrderId} not found.");
        if (string.IsNullOrWhiteSpace(order.OrderRef))
            return Result<ResendOmsTripFailedNotificationResult>.Failure(
                "Order has no OrderRef — only upstream-originated orders can be resent to OMS.");

        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<ResendOmsTripFailedNotificationResult>.Failure($"Trip {request.TripId} not found.");
        if (trip.DeliveryOrderId != request.OrderId)
            return Result<ResendOmsTripFailedNotificationResult>.Failure(
                $"Trip {request.TripId} does not belong to order {request.OrderId}.");
        if (trip.Status != TripStatus.Failed)
            return Result<ResendOmsTripFailedNotificationResult>.Failure(
                $"Trip {request.TripId} is not in Failed state (current: {trip.Status}). Cannot resend failure notification.");

        var rootTripId = await _tripRepository.GetRootTripIdAsync(trip.Id, cancellationToken);
        var shipmentId = rootTripId.ToString();
        var payload = new OmsTripFailedNotification(
            FailureReason: trip.FailureReason ?? "(reason not recorded)",
            FailureCategory: "TripFailed",
            OccurredAt: trip.CompletedAt ?? DateTime.UtcNow);

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentFailedAsync(shipmentId, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[OmsFailedResend] Trip {TripId} manual resend failed: {Error}",
                trip.Id, ex.Message);
            return Result<ResendOmsTripFailedNotificationResult>.Failure(
                $"OMS request failed: {ex.Message}");
        }
        sw.Stop();

        var auditDetails = $"trip-failed shipmentId={shipmentId} attempt={trip.AttemptNumber} reason=\"{Truncate(trip.FailureReason, 200)}\" latencyMs={sw.ElapsedMilliseconds}";
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
            "[OmsFailedResend] Trip {TripId} (attempt {N}) → OMS event=ManualFailedResend outcome=Success shipmentId={Sid} latencyMs={Ms} by={By}",
            trip.Id, trip.AttemptNumber, shipmentId, sw.ElapsedMilliseconds,
            request.RequestedBy ?? "(anonymous)");

        return Result<ResendOmsTripFailedNotificationResult>.Success(new ResendOmsTripFailedNotificationResult(
            ShipmentId: shipmentId, LatencyMs: sw.ElapsedMilliseconds));
    }

    private static string Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
