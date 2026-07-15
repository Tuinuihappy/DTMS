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

namespace DTMS.DeliveryOrder.Application.Commands.ResendOmsNotification;

public class ResendOmsNotificationCommandHandler
    : ICommandHandler<ResendOmsNotificationCommand, ResendOmsNotificationResult>
{
    private const string AuditEventType = "UpstreamOmsManuallyResent";
    // Federated OMS started-callback formatter key (see OmsShipmentStartedFormatter).
    // Kept as a local literal on purpose: [FromKeyedServices] needs a compile-time
    // const, and the formatter's own FormatKey lives in Iam.Infrastructure, which
    // this Application layer must not reference.
    private const string StartedFormatKey = "oms.shipment.started.v1";

    private readonly ICallbackPayloadFormatter _formatter;
    private readonly ISourceCallbackDispatcher _dispatcher;
    private readonly ISubscriptionLookup _lookup;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<ResendOmsNotificationCommandHandler> _logger;

    public ResendOmsNotificationCommandHandler(
        [FromKeyedServices(StartedFormatKey)] ICallbackPayloadFormatter formatter,
        ISourceCallbackDispatcher dispatcher,
        ISubscriptionLookup lookup,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<ResendOmsNotificationCommandHandler> logger)
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

    public async Task<Result<ResendOmsNotificationResult>> Handle(
        ResendOmsNotificationCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ResendOmsNotificationResult>.Failure($"Order {request.OrderId} not found.");

        if (string.IsNullOrWhiteSpace(order.OrderRef))
        {
            return Result<ResendOmsNotificationResult>.Failure(
                "Order has no OrderRef — only upstream-originated orders can be resent to OMS.");
        }

        // S.3.1b-followup guard — legacy adapter is OMS-only. Sap/Erp
        // orders carry an OrderRef too but route through the S.3.1b
        // SystemEventSubscriptions pipeline; resending them here would
        // wrongly POST to OMS.
        if (!string.Equals(order.SourceSystemKey, WellKnownSourceSystems.Oms, StringComparison.Ordinal))
        {
            return Result<ResendOmsNotificationResult>.Failure(
                $"Order is from {order.SourceSystemKey}, not OMS. Use the federated callback admin tools (Phase S.3.1b) to manage non-OMS notifications.");
        }

        // The oms subscription's Enabled is the sole off-switch for OMS callbacks
        // (Phase 4 removed UpstreamOms__Enabled). The auto fan-out honours it via
        // this same lookup; without the check here a manual resend would punch
        // straight through an emergency stop. Covers "disabled" and "no row" alike
        // — the lookup filters Enabled at SQL, so both yield an empty list.
        var subs = await _lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentStartedV1, cancellationToken);
        if (!subs.Any(s => string.Equals(s.SystemKey, WellKnownSourceSystems.Oms, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<ResendOmsNotificationResult>.Failure(
                "OMS shipment-started callbacks are disabled (subscription off or not configured). Enable the subscription before resending.");
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
        //
        // Self-managed orders are exempt: the source system executes the
        // transport itself so there is no vendor vehicle. Resend with
        // DeliveryBy=RequestedBy (the external actor; parity with
        // TripStartedOmsNotifyConsumer) rather than blocking on a robot name
        // that will never arrive.
        if (!order.SelfManaged && string.IsNullOrWhiteSpace(trip.VendorVehicleName))
        {
            return Result<ResendOmsNotificationResult>.Failure(
                "Trip has no VendorVehicleName yet — vendor has not reported the assigned robot. Retry after the trip starts.");
        }
        // DeliveryBy: AMR sends the vendor robot name; self-managed sends the
        // order's RequestedBy (the external actor — there is no vendor vehicle).
        var deliveryBy = order.SelfManaged ? order.RequestedBy : trip.VendorVehicleName;

        // [Option A] Use root tripId so manual resend updates the same
        // OMS shipment that the original /shipments POST registered.
        var rootTripId = await _tripRepository.GetRootTripIdAsync(trip.Id, cancellationToken);
        var shipmentId = rootTripId.ToString();

        // Format via the federated OMS formatter (byte-identical to legacy) and
        // dispatch SYNCHRONOUSLY through the shared callback dispatcher so the
        // operator sees the result immediately (2xx/409 → success, else fail).
        var context = new OmsShipmentStartedContext(shipmentId, deliveryBy, lots);
        var payload = await _formatter.FormatAsync(context, cancellationToken);
        var msg = new OutboxMessage(
            id: Guid.NewGuid(),
            type: CallbackEventTypes.ShipmentStartedV1,
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
                "[OmsResend] Trip {TripId} manual resend failed ({Status}): {Error}",
                trip.Id, status, ex.Message);
            return Result<ResendOmsNotificationResult>.Failure(
                status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
                    ? $"OMS rejected the request ({(int)status}): {ex.Message}. Fix the data at upstream before resending."
                    : $"OMS request failed: {ex.Message}");
        }
        sw.Stop();

        var auditDetails = $"trip-started shipmentId={shipmentId} attempt={trip.AttemptNumber} vehicle={deliveryBy} lots={lots.Count} latencyMs={sw.ElapsedMilliseconds}";
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
            trip.Id, trip.AttemptNumber, shipmentId, deliveryBy, lots.Count, sw.ElapsedMilliseconds,
            request.RequestedBy ?? "(anonymous)");

        return Result<ResendOmsNotificationResult>.Success(new ResendOmsNotificationResult(
            ShipmentId: shipmentId,
            DeliveryBy: deliveryBy,
            LotCount: lots.Count,
            LatencyMs: sw.ElapsedMilliseconds));
    }
}
