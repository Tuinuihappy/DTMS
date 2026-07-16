using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.ResendOmsArrivedNotification;

public class ResendOmsArrivedNotificationCommandHandler
    : ICommandHandler<ResendOmsArrivedNotificationCommand, ResendOmsArrivedNotificationResult>
{
    private const string AuditEventType = "UpstreamOmsArrivedManuallyResent";
    // Federated OMS arrived-callback formatter key (see OmsShipmentArrivedFormatter).
    // Kept as a local literal on purpose: [FromKeyedServices] needs a compile-time
    // const, and the formatter's own FormatKey lives in Iam.Infrastructure, which
    // this Application layer must not reference.
    private const string ArrivedFormatKey = "oms.shipment.arrived.v1";

    private readonly ICallbackPayloadFormatter _formatter;
    private readonly ISourceCallbackDispatcher _dispatcher;
    private readonly ISubscriptionLookup _lookup;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<ResendOmsArrivedNotificationCommandHandler> _logger;

    public ResendOmsArrivedNotificationCommandHandler(
        [FromKeyedServices(ArrivedFormatKey)] ICallbackPayloadFormatter formatter,
        ISourceCallbackDispatcher dispatcher,
        ISubscriptionLookup lookup,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<ResendOmsArrivedNotificationCommandHandler> logger)
    {
        _formatter = formatter;
        _dispatcher = dispatcher;
        _lookup = lookup;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task<Result<ResendOmsArrivedNotificationResult>> Handle(
        ResendOmsArrivedNotificationCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ResendOmsArrivedNotificationResult>.Failure($"Order {request.OrderId} not found.");

        if (string.IsNullOrWhiteSpace(order.OrderRef))
        {
            return Result<ResendOmsArrivedNotificationResult>.Failure(
                "Order has no OrderRef — only upstream-originated orders can be resent to OMS.");
        }

        // S.3.1b-followup guard — legacy adapter is OMS-only. Sap/Erp
        // orders carry an OrderRef too but route through the S.3.1b
        // SystemEventSubscriptions pipeline.
        if (!string.Equals(order.SourceSystemKey, WellKnownSourceSystems.Oms, StringComparison.Ordinal))
        {
            return Result<ResendOmsArrivedNotificationResult>.Failure(
                $"Order is from {order.SourceSystemKey}, not OMS. Use the federated callback admin tools (Phase S.3.1b) to manage non-OMS notifications.");
        }

        // The oms subscription's Enabled is the sole off-switch for OMS callbacks
        // (Phase 4 removed UpstreamOms__Enabled). The auto fan-out honours it via
        // this same lookup; without the check here a manual resend would punch
        // straight through an emergency stop. Covers "disabled" and "no row" alike
        // — the lookup filters Enabled at SQL, so both yield an empty list.
        var subs = await _lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentArrivedV1, cancellationToken);
        if (!subs.Any(s => string.Equals(s.SystemKey, WellKnownSourceSystems.Oms, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<ResendOmsArrivedNotificationResult>.Failure(
                "OMS shipment-arrived callbacks are disabled (subscription off or not configured). Enable the subscription before resending.");
        }

        // Manual transport does not report arrival to OMS (parity with the auto
        // ShipmentArrivedCallbackFanoutConsumer) — OMS owns the arrival signal
        // for operator-pool / self-managed deliveries.
        if (order.RequestedTransportMode == TransportMode.Manual)
        {
            return Result<ResendOmsArrivedNotificationResult>.Failure(
                "Manual transport does not send arrival notifications to OMS.");
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

        // Format via the federated OMS arrived formatter (byte-identical to
        // legacy — shipmentId in the path, lots in the body) and dispatch
        // SYNCHRONOUSLY so the operator sees the result immediately.
        var context = new ShipmentArrivedContext(shipmentId, lots);
        var payload = await _formatter.FormatAsync(context, cancellationToken);
        var msg = new OutboxMessage(
            id: Guid.NewGuid(),
            type: CallbackEventTypes.ShipmentArrivedV1,
            content: Encoding.UTF8.GetString(payload.Body),
            occurredOnUtc: DateTime.UtcNow,
            partitionKey: WellKnownSourceSystems.Oms,
            callbackPath: payload.RelativePath,
            callbackMethod: payload.HttpMethod,
            relatedOrderId: order.Id,
            relatedTripId: trip.Id);

        var sw = Stopwatch.StartNew();
        try
        {
            await _dispatcher.DispatchAsync(WellKnownSourceSystems.Oms, msg, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var status = (ex as HttpRequestException)?.StatusCode;
            _logger.LogWarning(ex,
                "[OmsArrivedResend] Trip {TripId} manual resend failed ({Status}): {Error}",
                trip.Id, status, ex.Message);
            return Result<ResendOmsArrivedNotificationResult>.Failure(
                status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
                    ? $"OMS rejected the request ({(int)status}): {ex.Message}. Fix the data at upstream before resending."
                    : $"OMS request failed: {ex.Message}");
        }
        sw.Stop();

        var auditDetails = $"trip-arrived shipmentId={shipmentId} attempt={trip.AttemptNumber} lots={lots.Count} latencyMs={sw.ElapsedMilliseconds}";
        await _auditRepository.AddAsync(new OrderAuditEvent(
            order.Id, AuditEventType, auditDetails, actorId: request.RequestedBy),
            cancellationToken);
        await _auditRepository.SaveChangesAsync(cancellationToken);

        // P2.5 mirror: see ResendOmsNotificationCommandHandler for rationale.
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
            "[OmsArrivedResend] Trip {TripId} (attempt {N}) → OMS event=ManualArrivedResend outcome=Success shipmentId={Sid} lots={LotCount} latencyMs={Ms} by={By}",
            trip.Id, trip.AttemptNumber, shipmentId, lots.Count, sw.ElapsedMilliseconds,
            request.RequestedBy ?? "(anonymous)");

        return Result<ResendOmsArrivedNotificationResult>.Success(new ResendOmsArrivedNotificationResult(
            ShipmentId: shipmentId,
            LotCount: lots.Count,
            LatencyMs: sw.ElapsedMilliseconds));
    }
}
