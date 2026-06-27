using System.Diagnostics;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.OmsAdapter.Abstractions;
using DTMS.OmsAdapter.Abstractions.Exceptions;
using DTMS.OmsAdapter.Abstractions.Models;
using DTMS.OmsAdapter.Infrastructure.Options;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.DeliveryOrder.Application.Commands.ResendOmsNotification;

public class ResendOmsNotificationCommandHandler
    : ICommandHandler<ResendOmsNotificationCommand, ResendOmsNotificationResult>
{
    private const string AuditEventType = "UpstreamOmsManuallyResent";

    private readonly UpstreamOmsOptions _options;
    private readonly IOmsShipmentClient _client;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<ResendOmsNotificationCommandHandler> _logger;

    public ResendOmsNotificationCommandHandler(
        IOptions<UpstreamOmsOptions> options,
        IOmsShipmentClient client,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<ResendOmsNotificationCommandHandler> logger)
    {
        _options = options.Value;
        _client = client;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
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

        // Name + Key are captured together from RIOT3 TASK_PROCESSING
        // (first-write-wins). A missing Name means the vendor hasn't
        // reported the assigned robot yet — return Failure so the operator
        // sees a clear reason instead of OMS receiving a "(unknown)"
        // placeholder that would clobber a prior real value.
        if (string.IsNullOrWhiteSpace(trip.VendorVehicleName))
        {
            return Result<ResendOmsNotificationResult>.Failure(
                "Trip has no VendorVehicleName yet — vendor has not reported the assigned robot. Retry after the trip starts.");
        }
        var vendorVehicleName = trip.VendorVehicleName;

        // [Option A] Use root tripId so manual resend updates the same
        // OMS shipment that the original /shipments POST registered.
        var rootTripId = await _tripRepository.GetRootTripIdAsync(trip.Id, cancellationToken);
        var shipmentId = rootTripId.ToString();

        var payload = new OmsShipmentNotification(
            ShipmentId: shipmentId,
            DeliveryBy: vendorVehicleName,
            Lots: lots.Select(id => new OmsLot(id)).ToList());

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.NotifyShipmentStartedAsync(payload, cancellationToken);
        }
        catch (OmsPermanentException ex)
        {
            sw.Stop();
            var statusCode = (int?)ex.StatusCode ?? 0;
            _logger.LogWarning(ex,
                "[OmsResend] Trip {TripId} rejected by OMS ({Status}): {Body}",
                trip.Id, statusCode, ex.ResponseBody);
            return Result<ResendOmsNotificationResult>.Failure(
                $"OMS rejected the data ({statusCode}): {ex.ResponseBody}. " +
                "Fix the data at upstream (SAP/ERP/OMS) before resending — retrying with the same payload will fail again.");
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

        var auditDetails = $"trip-started shipmentId={shipmentId} attempt={trip.AttemptNumber} vehicle={vendorVehicleName} lots={lots.Count} latencyMs={sw.ElapsedMilliseconds}";
        await _auditRepository.AddAsync(new OrderAuditEvent(
            order.Id, AuditEventType, auditDetails, actorId: request.RequestedBy),
            cancellationToken);
        await _auditRepository.SaveChangesAsync(cancellationToken);

        // P2.5 mirror: OmsNotify outcomes don't flow through an integration
        // event, so the OrderActivity projector can't pick them up. Write
        // directly to the unified audit timeline so the OmsNotificationSection
        // UI (which reads OrderActivity) reflects the resend.
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
            "[OmsResend] Trip {TripId} (attempt {N}) → OMS event=ManualResend outcome=Success shipmentId={Sid} vehicle={VehName} lots={LotCount} latencyMs={Ms} by={By}",
            trip.Id, trip.AttemptNumber, shipmentId, vendorVehicleName, lots.Count, sw.ElapsedMilliseconds,
            request.RequestedBy ?? "(anonymous)");

        return Result<ResendOmsNotificationResult>.Success(new ResendOmsNotificationResult(
            ShipmentId: shipmentId,
            DeliveryBy: vendorVehicleName,
            LotCount: lots.Count,
            LatencyMs: sw.ElapsedMilliseconds));
    }
}
