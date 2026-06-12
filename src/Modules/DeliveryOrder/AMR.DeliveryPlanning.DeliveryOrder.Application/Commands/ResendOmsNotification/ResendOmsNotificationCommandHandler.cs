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

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ResendOmsNotification;

public class ResendOmsNotificationCommandHandler
    : ICommandHandler<ResendOmsNotificationCommand, ResendOmsNotificationResult>
{
    private const string AuditEventType = "UpstreamOmsManuallyResent";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly ILogger<ResendOmsNotificationCommandHandler> _logger;

    public ResendOmsNotificationCommandHandler(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        ILogger<ResendOmsNotificationCommandHandler> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    public async Task<Result<ResendOmsNotificationResult>> Handle(
        ResendOmsNotificationCommand request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Result<ResendOmsNotificationResult>.Failure(
                "Upstream OMS notifications are disabled. Toggle UpstreamOms:Enabled to resend.");
        }

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ResendOmsNotificationResult>.Failure($"Order {request.OrderId} not found.");

        if (string.IsNullOrWhiteSpace(order.OrderRef))
        {
            return Result<ResendOmsNotificationResult>.Failure(
                "Order has no OrderRef — only upstream-originated orders can be resent to OMS.");
        }

        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<ResendOmsNotificationResult>.Failure($"Trip {request.TripId} not found.");

        if (trip.DeliveryOrderId != request.OrderId)
        {
            return Result<ResendOmsNotificationResult>.Failure(
                $"Trip {request.TripId} does not belong to order {request.OrderId}.");
        }

        var lots = order.Items
            .Where(i => i.TripId == request.TripId)
            .Select(i => i.ItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (lots.Count == 0)
        {
            return Result<ResendOmsNotificationResult>.Failure(
                "No items are bound to this trip — nothing to send.");
        }

        var vendorVehicleKey = string.IsNullOrWhiteSpace(trip.VendorVehicleKey)
            ? "(unknown)"
            : trip.VendorVehicleKey;

        // [Option A] Use root tripId so manual resend updates the same
        // OMS shipment that the original /shipments POST registered.
        var rootTripId = await _tripRepository.GetRootTripIdAsync(trip.Id, cancellationToken);
        var shipmentId = rootTripId.ToString();

        var payload = new OmsShipmentNotification(
            ShipmentId: shipmentId,
            DeliveryBy: vendorVehicleKey,
            Lots: lots.Select(id => new OmsLot(id)).ToList());

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentStartedAsync(payload, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[OmsResend] Trip {TripId} manual resend failed: {Error}",
                trip.Id, ex.Message);
            return Result<ResendOmsNotificationResult>.Failure(
                $"OMS request failed: {ex.Message}");
        }
        sw.Stop();

        await _auditRepository.AddAsync(new OrderAuditEvent(
            order.Id,
            AuditEventType,
            $"trip-started shipmentId={shipmentId} attempt={trip.AttemptNumber} vehicle={vendorVehicleKey} lots={lots.Count} latencyMs={sw.ElapsedMilliseconds}",
            actorId: request.RequestedBy),
            cancellationToken);
        await _auditRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[OmsResend] Trip {TripId} (attempt {N}) → OMS event=ManualResend outcome=Success shipmentId={Sid} vehicle={VehKey} lots={LotCount} latencyMs={Ms} by={By}",
            trip.Id, trip.AttemptNumber, shipmentId, vendorVehicleKey, lots.Count, sw.ElapsedMilliseconds,
            request.RequestedBy ?? "(anonymous)");

        return Result<ResendOmsNotificationResult>.Success(new ResendOmsNotificationResult(
            ShipmentId: shipmentId,
            DeliveryBy: vendorVehicleKey,
            LotCount: lots.Count,
            LatencyMs: sw.ElapsedMilliseconds));
    }
}
