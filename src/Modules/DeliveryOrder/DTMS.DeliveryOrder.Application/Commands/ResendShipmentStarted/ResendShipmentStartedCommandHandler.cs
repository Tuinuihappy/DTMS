using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using DTMS.DeliveryOrder.Application.Consumers;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Outbox;
using DTMS.Transport.Manual.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.ResendShipmentStarted;

public class ResendShipmentStartedCommandHandler
    : ICommandHandler<ResendShipmentStartedCommand, ResendShipmentStartedResult>
{
    private readonly ICallbackFormatterResolver _formatterResolver;
    private readonly ISourceCallbackDispatcher _dispatcher;
    private readonly ISubscriptionLookup _lookup;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOperatorRepository _operatorRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ISourceCallbackOutboxSuperseder _outboxSuperseder;
    private readonly ILogger<ResendShipmentStartedCommandHandler> _logger;

    public ResendShipmentStartedCommandHandler(
        ICallbackFormatterResolver formatterResolver,
        ISourceCallbackDispatcher dispatcher,
        ISubscriptionLookup lookup,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOperatorRepository operatorRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ISourceCallbackOutboxSuperseder outboxSuperseder,
        ILogger<ResendShipmentStartedCommandHandler> logger)
    {
        _formatterResolver = formatterResolver;
        _dispatcher = dispatcher;
        _lookup = lookup;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _operatorRepository = operatorRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _outboxSuperseder = outboxSuperseder;
        _logger = logger;
    }

    public async Task<Result<ResendShipmentStartedResult>> Handle(
        ResendShipmentStartedCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ResendShipmentStartedResult>.Failure($"Order {request.OrderId} not found.");

        if (string.IsNullOrWhiteSpace(order.OrderRef))
        {
            return Result<ResendShipmentStartedResult>.Failure(
                "Order has no OrderRef — only upstream-originated orders can be resent to their source system.");
        }

        // Phase C — the target system comes from the ORDER, not a hardcoded
        // key. The subscription is both the routing record (which formatter,
        // which partition) and the off-switch: no enabled row for this source
        // + event type means the resend must refuse, exactly like the auto
        // fan-out silently skips. Covers "disabled" and "not configured"
        // alike — the lookup filters Enabled at SQL.
        var source = order.SourceSystemKey;
        if (string.IsNullOrWhiteSpace(source))
        {
            return Result<ResendShipmentStartedResult>.Failure(
                "Order has no source system — nothing to notify.");
        }

        var subs = await _lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentStartedV1, cancellationToken);
        var sub = subs.FirstOrDefault(s => string.Equals(s.SystemKey, source, StringComparison.OrdinalIgnoreCase));
        if (sub is null)
        {
            return Result<ResendShipmentStartedResult>.Failure(
                $"Shipment-started callbacks for '{source}' are disabled (subscription off or not configured). Enable the subscription before resending.");
        }

        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<ResendShipmentStartedResult>.Failure($"Trip {request.TripId} not found.");

        if (trip.DeliveryOrderId != request.OrderId)
        {
            return Result<ResendShipmentStartedResult>.Failure(
                $"Trip {request.TripId} does not belong to order {request.OrderId}.");
        }

        // deliveryBy — same 4-rung ladder as the auto fan-out
        // (ShipmentStartedCallbackFanoutConsumer): self-managed → RequestedBy;
        // pool claimed → the operator; pool unclaimed → null; AMR → vendor
        // vehicle name. The AMR rung stays a Failure (not a throw) so the
        // operator sees a clear reason instead of the upstream receiving a
        // placeholder that would clobber a prior real value.
        string? deliveryBy;
        if (order.SelfManaged)
        {
            deliveryBy = order.RequestedBy;
            if (string.IsNullOrWhiteSpace(deliveryBy))
            {
                _logger.LogWarning(
                    "[ShipmentStartedResend] Order {OrderId} is self-managed with no RequestedBy — resending with deliveryBy=null (source system should supply RequestedBy)",
                    order.Id);
            }
        }
        else if (trip.ClaimedByOperatorId is Guid operatorId)
        {
            var op = await _operatorRepository.GetByIdAsync(operatorId, cancellationToken);
            deliveryBy = op is null ? null : $"{op.DisplayName} ({op.EmployeeCode})";
            if (op is null)
            {
                _logger.LogWarning(
                    "[ShipmentStartedResend] Trip {TripId} claimed by operator {OperatorId} but no Operator row exists — resending with deliveryBy=null",
                    trip.Id, operatorId);
            }
        }
        else if (trip.DispatchedAt is not null)
        {
            // Pool trip that never got (or hasn't yet got) an operator claim —
            // resendable with a null carrier; upstreams accept that shape.
            deliveryBy = null;
        }
        else if (string.IsNullOrWhiteSpace(trip.VendorVehicleName))
        {
            return Result<ResendShipmentStartedResult>.Failure(
                "Trip has no VendorVehicleName yet — vendor has not reported the assigned robot. Retry after the trip starts.");
        }
        else
        {
            deliveryBy = trip.VendorVehicleName;
        }

        // [Option A] Use root tripId so manual resend updates the same
        // shipment that the original started callback registered.
        var rootTripId = await _tripRepository.GetRootTripIdAsync(trip.Id, cancellationToken);
        var shipmentId = rootTripId.ToString();

        // Format via the SUBSCRIPTION's formatter (sap's row names sap's
        // formatter — the same resolution the fan-out does) and dispatch
        // SYNCHRONOUSLY so the operator sees the result immediately
        // (2xx/409 → success, else fail).
        var formatter = _formatterResolver.Resolve(sub.PayloadFormatKey);
        // Resend has no source event to read a timestamp from — use the trip's
        // own start time so a resend hours later still reports when the
        // shipment actually started. DispatchedAt covers a pool trip resent
        // before anyone claimed it; UtcNow is the never-started edge case.
        var occurredAt = trip.StartedAt ?? trip.DispatchedAt ?? DateTime.UtcNow;
        var context = new ShipmentStartedContext(shipmentId, order.OrderRef!, deliveryBy, occurredAt);
        var payload = await formatter.FormatAsync(context, cancellationToken);
        var msg = new OutboxMessage(
            id: Guid.NewGuid(),
            type: CallbackEventTypes.ShipmentStartedV1,
            content: Encoding.UTF8.GetString(payload.Body),
            occurredOnUtc: DateTime.UtcNow,
            partitionKey: sub.SystemKey,
            callbackPath: payload.RelativePath,
            callbackMethod: payload.HttpMethod,
            relatedOrderId: order.Id,
            relatedTripId: trip.Id);

        var sw = Stopwatch.StartNew();
        try
        {
            await _dispatcher.DispatchAsync(sub.SystemKey, msg, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var status = (ex as HttpRequestException)?.StatusCode;
            _logger.LogWarning(ex,
                "[ShipmentStartedResend] Trip {TripId} manual resend to {System} failed ({Status}): {Error}",
                trip.Id, source, status, ex.Message);
            return Result<ResendShipmentStartedResult>.Failure(
                status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
                    ? $"{source} rejected the request ({(int)status}): {ex.Message}. Fix the data at upstream before resending."
                    : $"Callback to {source} failed: {ex.Message}");
        }
        sw.Stop();

        // F2 — from here on the upstream HAS the callback; the audit/activity
        // writes are best-effort (each guarded separately so an audit failure
        // doesn't also drop the activity row the UI reads). A persistence
        // hiccup is logged, never surfaced as a resend failure. Re-clicking
        // stays harmless — upstreams dedupe by shipmentId (409 = success).
        var auditDetails = $"trip-started shipmentId={shipmentId} attempt={trip.AttemptNumber} vehicle={deliveryBy} orderRef={order.OrderRef} latencyMs={sw.ElapsedMilliseconds}";
        try
        {
            await _auditRepository.AddAsync(new OrderAuditEvent(
                order.Id, UpstreamCallbackAudit.ManuallyResent, auditDetails,
                actorId: request.RequestedBy, systemKey: source),
                cancellationToken);
            await _auditRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "[ShipmentStartedResend] Trip {TripId} resend DELIVERED to {System} but the audit write failed — timeline may miss it",
                trip.Id, source);
        }

        // P2.5 mirror: resend outcomes don't flow through an integration
        // event, so the OrderActivity projector can't pick them up. Write
        // directly to the unified timeline the upstream-notification UI reads.
        try
        {
            await _activityStore.AppendAsync(
                projectorName: UpstreamCallbackAudit.ProjectorName,
                eventId: Guid.NewGuid(),
                orderId: order.Id,
                category: UpstreamCallbackAudit.Category,
                eventType: UpstreamCallbackAudit.ManuallyResent,
                details: auditDetails,
                actorId: request.RequestedBy,
                occurredAt: DateTime.UtcNow,
                relatedTripId: trip.Id,
                attemptNumber: trip.AttemptNumber,
                cancellationToken: cancellationToken,
                systemKey: source);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "[ShipmentStartedResend] Trip {TripId} resend DELIVERED to {System} but the activity write failed — UI may not reflect it",
                trip.Id, source);
        }

        // The resend just delivered shipment.started out-of-band. Any fan-out
        // row still queued for this order+system would, on its next retry,
        // re-POST the same shipment and draw OMS's create-once 400 — which
        // clobbers this success with a red card. Retire those pending rows
        // now. Best-effort: a failure here only risks the old (pre-fix)
        // behaviour, never the resend itself.
        try
        {
            // Use sub.SystemKey (the exact value the fan-out wrote to
            // PartitionKey), not `source` — the subscription lookup matches
            // OrdinalIgnoreCase, so order.SourceSystemKey may differ in casing
            // and a case-sensitive SQL match would silently miss the row.
            var retired = await _outboxSuperseder.SupersedePendingAsync(
                sub.SystemKey, CallbackEventTypes.ShipmentStartedV1, order.Id, cancellationToken);
            if (retired > 0)
                _logger.LogInformation(
                    "[ShipmentStartedResend] Trip {TripId} resend superseded {Count} pending outbox row(s) for order {OrderId} → {System}",
                    trip.Id, retired, order.Id, source);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "[ShipmentStartedResend] Trip {TripId} resend DELIVERED to {System} but superseding pending outbox rows failed — a queued retry may re-POST and surface a duplicate 400",
                trip.Id, source);
        }

        _logger.LogInformation(
            "[ShipmentStartedResend] Trip {TripId} (attempt {N}) → {System} outcome=Success shipmentId={Sid} vehicle={VehName} latencyMs={Ms} by={By}",
            trip.Id, trip.AttemptNumber, source, shipmentId, deliveryBy, sw.ElapsedMilliseconds,
            request.RequestedBy ?? "(anonymous)");

        return Result<ResendShipmentStartedResult>.Success(new ResendShipmentStartedResult(
            ShipmentId: shipmentId,
            DeliveryBy: deliveryBy,
            LatencyMs: sw.ElapsedMilliseconds));
    }
}
