using System.Diagnostics;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.OmsAdapter.Infrastructure.Options;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ResendOmsArrivedNotification;

public class ResendOmsArrivedNotificationCommandHandler
    : ICommandHandler<ResendOmsArrivedNotificationCommand, ResendOmsArrivedNotificationResult>
{
    private const string AuditEventType = "UpstreamOmsArrivedManuallyResent";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly ILogger<ResendOmsArrivedNotificationCommandHandler> _logger;

    public ResendOmsArrivedNotificationCommandHandler(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        ILogger<ResendOmsArrivedNotificationCommandHandler> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    public async Task<Result<ResendOmsArrivedNotificationResult>> Handle(
        ResendOmsArrivedNotificationCommand request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Result<ResendOmsArrivedNotificationResult>.Failure(
                "Upstream OMS notifications are disabled. Toggle UpstreamOms:Enabled to resend.");
        }

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ResendOmsArrivedNotificationResult>.Failure($"Order {request.OrderId} not found.");

        if (string.IsNullOrWhiteSpace(order.OrderRef))
        {
            return Result<ResendOmsArrivedNotificationResult>.Failure(
                "Order has no OrderRef — only upstream-originated orders can be resent to OMS.");
        }

        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<ResendOmsArrivedNotificationResult>.Failure($"Trip {request.TripId} not found.");

        if (trip.DeliveryOrderId != request.OrderId)
        {
            return Result<ResendOmsArrivedNotificationResult>.Failure(
                $"Trip {request.TripId} does not belong to order {request.OrderId}.");
        }

        var lots = order.Items
            .Where(i => i.TripId == request.TripId)
            .Select(i => i.ItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (lots.Count == 0)
        {
            return Result<ResendOmsArrivedNotificationResult>.Failure(
                "No items are bound to this trip — nothing to send.");
        }

        // [Option A] Stable shipmentId across retry chain.
        var rootTripId = await _tripRepository.GetRootTripIdAsync(trip.Id, cancellationToken);
        var shipmentId = rootTripId.ToString();
        var lotPayload = lots.Select(id => new OmsLot(id)).ToList();

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentArrivedAsync(shipmentId, lotPayload, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[OmsArrivedResend] Trip {TripId} manual resend failed: {Error}",
                trip.Id, ex.Message);
            return Result<ResendOmsArrivedNotificationResult>.Failure(
                $"OMS request failed: {ex.Message}");
        }
        sw.Stop();

        await _auditRepository.AddAsync(new OrderAuditEvent(
            order.Id,
            AuditEventType,
            $"trip-arrived shipmentId={shipmentId} attempt={trip.AttemptNumber} lots={lots.Count} latencyMs={sw.ElapsedMilliseconds}",
            actorId: request.RequestedBy),
            cancellationToken);
        await _auditRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[OmsArrivedResend] Trip {TripId} (attempt {N}) → OMS event=ManualArrivedResend outcome=Success shipmentId={Sid} lots={LotCount} latencyMs={Ms} by={By}",
            trip.Id, trip.AttemptNumber, shipmentId, lots.Count, sw.ElapsedMilliseconds,
            request.RequestedBy ?? "(anonymous)");

        return Result<ResendOmsArrivedNotificationResult>.Success(new ResendOmsArrivedNotificationResult(
            ShipmentId: shipmentId,
            LotCount: lots.Count,
            LatencyMs: sw.ElapsedMilliseconds));
    }
}
