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
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.ResendShipmentPickedUp;

public class ResendShipmentPickedUpCommandHandler
    : ICommandHandler<ResendShipmentPickedUpCommand, ResendShipmentPickedUpResult>
{
    private readonly ICallbackFormatterResolver _formatterResolver;
    private readonly ISourceCallbackDispatcher _dispatcher;
    private readonly ISubscriptionLookup _lookup;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ISourceCallbackOutboxSuperseder _outboxSuperseder;
    private readonly ILogger<ResendShipmentPickedUpCommandHandler> _logger;

    public ResendShipmentPickedUpCommandHandler(
        ICallbackFormatterResolver formatterResolver,
        ISourceCallbackDispatcher dispatcher,
        ISubscriptionLookup lookup,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ISourceCallbackOutboxSuperseder outboxSuperseder,
        ILogger<ResendShipmentPickedUpCommandHandler> logger)
    {
        _formatterResolver = formatterResolver;
        _dispatcher = dispatcher;
        _lookup = lookup;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _outboxSuperseder = outboxSuperseder;
        _logger = logger;
    }

    public async Task<Result<ResendShipmentPickedUpResult>> Handle(
        ResendShipmentPickedUpCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ResendShipmentPickedUpResult>.Failure($"Order {request.OrderId} not found.");

        if (string.IsNullOrWhiteSpace(order.OrderRef))
        {
            return Result<ResendShipmentPickedUpResult>.Failure(
                "Order has no OrderRef — only upstream-originated orders can be resent to their source system.");
        }

        // Self-managed pickup auto-fires at dispatch and carries no physical
        // meaning — the auto fan-out skips it, so the resend must refuse too.
        if (order.SelfManaged)
        {
            return Result<ResendShipmentPickedUpResult>.Failure(
                "Self-managed orders do not send pickup notifications — the source system executes the pickup itself.");
        }

        // Phase C — target system from the ORDER; the subscription row is
        // routing record + off-switch in one (see the started handler).
        var source = order.SourceSystemKey;
        if (string.IsNullOrWhiteSpace(source))
        {
            return Result<ResendShipmentPickedUpResult>.Failure(
                "Order has no source system — nothing to notify.");
        }

        var subs = await _lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentPickedUpV1, cancellationToken);
        var sub = subs.FirstOrDefault(s => string.Equals(s.SystemKey, source, StringComparison.OrdinalIgnoreCase));
        if (sub is null)
        {
            return Result<ResendShipmentPickedUpResult>.Failure(
                $"Shipment-pickedup callbacks for '{source}' are disabled (subscription off or not configured). Enable the subscription before resending.");
        }

        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<ResendShipmentPickedUpResult>.Failure($"Trip {request.TripId} not found.");

        if (trip.DeliveryOrderId != request.OrderId)
        {
            return Result<ResendShipmentPickedUpResult>.Failure(
                $"Trip {request.TripId} does not belong to order {request.OrderId}.");
        }

        // A trip that never reported pickup has nothing truthful to resend —
        // occurredAt would be fabricated.
        if (trip.VendorPickedUpAt is null)
        {
            return Result<ResendShipmentPickedUpResult>.Failure(
                "Trip has not reported pickup yet — nothing to resend.");
        }

        // locationCode is REQUIRED by the endpoint: the pickup code the source
        // system itself submitted on the order items (Item.PickupLocationCode).
        var locationCode = order.Items
            .Where(i => i.TripId == request.TripId)
            .Select(i => i.PickupLocationCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .FirstOrDefault();
        if (locationCode is null)
        {
            return Result<ResendShipmentPickedUpResult>.Failure(
                "No items are bound to this trip — locationCode is required by the upstream endpoint.");
        }

        // [Option A] Stable shipmentId across retry chain.
        var rootTripId = await _tripRepository.GetRootTripIdAsync(trip.Id, cancellationToken);
        var shipmentId = rootTripId.ToString();

        // Format via the SUBSCRIPTION's formatter and dispatch SYNCHRONOUSLY
        // so the operator sees the result immediately.
        var formatter = _formatterResolver.Resolve(sub.PayloadFormatKey);
        var context = new ShipmentPickedUpContext(
            shipmentId, order.OrderRef!, locationCode, trip.VendorPickedUpAt.Value);
        var payload = await formatter.FormatAsync(context, cancellationToken);
        var msg = new OutboxMessage(
            id: Guid.NewGuid(),
            type: CallbackEventTypes.ShipmentPickedUpV1,
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
                "[ShipmentPickedUpResend] Trip {TripId} manual resend to {System} failed ({Status}): {Error}",
                trip.Id, source, status, ex.Message);
            return Result<ResendShipmentPickedUpResult>.Failure(
                status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
                    ? $"{source} rejected the request ({(int)status}): {ex.Message}. Fix the data at upstream before resending."
                    : $"Callback to {source} failed: {ex.Message}");
        }
        sw.Stop();

        // F2 — the upstream has the callback; audit/activity are best-effort
        // from here (see the started handler).
        var auditDetails = $"trip-pickedup shipmentId={shipmentId} attempt={trip.AttemptNumber} location={locationCode} latencyMs={sw.ElapsedMilliseconds}";
        try
        {
            await _auditRepository.AddAsync(new OrderAuditEvent(
                order.Id, UpstreamCallbackAudit.PickedUpManuallyResent, auditDetails,
                actorId: request.RequestedBy, systemKey: source),
                cancellationToken);
            await _auditRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "[ShipmentPickedUpResend] Trip {TripId} resend DELIVERED to {System} but the audit write failed — timeline may miss it",
                trip.Id, source);
        }

        try
        {
            await _activityStore.AppendAsync(
                projectorName: UpstreamCallbackAudit.ProjectorName,
                eventId: Guid.NewGuid(),
                orderId: order.Id,
                category: UpstreamCallbackAudit.Category,
                eventType: UpstreamCallbackAudit.PickedUpManuallyResent,
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
                "[ShipmentPickedUpResend] Trip {TripId} resend DELIVERED to {System} but the activity write failed — UI may not reflect it",
                trip.Id, source);
        }

        // Retire any fan-out row still queued for this order+system so its
        // next retry can't re-POST a duplicate and clobber this success.
        // Best-effort (see the started handler for the full rationale).
        try
        {
            // Use sub.SystemKey (the exact value the fan-out wrote to
            // PartitionKey), not `source` — casing may differ.
            var retired = await _outboxSuperseder.SupersedePendingAsync(
                sub.SystemKey, CallbackEventTypes.ShipmentPickedUpV1, order.Id, cancellationToken);
            if (retired > 0)
                _logger.LogInformation(
                    "[ShipmentPickedUpResend] Trip {TripId} resend superseded {Count} pending outbox row(s) for order {OrderId} → {System}",
                    trip.Id, retired, order.Id, source);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "[ShipmentPickedUpResend] Trip {TripId} resend DELIVERED to {System} but superseding pending outbox rows failed — a queued retry may re-POST a duplicate",
                trip.Id, source);
        }

        _logger.LogInformation(
            "[ShipmentPickedUpResend] Trip {TripId} (attempt {N}) → {System} outcome=Success shipmentId={Sid} location={LocationCode} latencyMs={Ms} by={By}",
            trip.Id, trip.AttemptNumber, source, shipmentId, locationCode, sw.ElapsedMilliseconds,
            request.RequestedBy ?? "(anonymous)");

        return Result<ResendShipmentPickedUpResult>.Success(new ResendShipmentPickedUpResult(
            ShipmentId: shipmentId,
            LocationCode: locationCode,
            LatencyMs: sw.ElapsedMilliseconds));
    }
}
