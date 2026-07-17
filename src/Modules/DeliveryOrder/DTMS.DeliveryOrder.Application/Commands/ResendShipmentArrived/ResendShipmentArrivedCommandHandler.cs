using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using DTMS.DeliveryOrder.Application.Consumers;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Outbox;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.ResendShipmentArrived;

public class ResendShipmentArrivedCommandHandler
    : ICommandHandler<ResendShipmentArrivedCommand, ResendShipmentArrivedResult>
{
    private readonly ICallbackFormatterResolver _formatterResolver;
    private readonly ISourceCallbackDispatcher _dispatcher;
    private readonly ISubscriptionLookup _lookup;
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderRepository _orderRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<ResendShipmentArrivedCommandHandler> _logger;

    public ResendShipmentArrivedCommandHandler(
        ICallbackFormatterResolver formatterResolver,
        ISourceCallbackDispatcher dispatcher,
        ISubscriptionLookup lookup,
        ITripRepository tripRepository,
        IDeliveryOrderRepository orderRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<ResendShipmentArrivedCommandHandler> logger)
    {
        _formatterResolver = formatterResolver;
        _dispatcher = dispatcher;
        _lookup = lookup;
        _tripRepository = tripRepository;
        _orderRepository = orderRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task<Result<ResendShipmentArrivedResult>> Handle(
        ResendShipmentArrivedCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ResendShipmentArrivedResult>.Failure($"Order {request.OrderId} not found.");

        if (string.IsNullOrWhiteSpace(order.OrderRef))
        {
            return Result<ResendShipmentArrivedResult>.Failure(
                "Order has no OrderRef — only upstream-originated orders can be resent to their source system.");
        }

        // Phase C — target system from the ORDER; the subscription row is
        // routing record + off-switch in one (see the started handler).
        var source = order.SourceSystemKey;
        if (string.IsNullOrWhiteSpace(source))
        {
            return Result<ResendShipmentArrivedResult>.Failure(
                "Order has no source system — nothing to notify.");
        }

        var subs = await _lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentArrivedV1, cancellationToken);
        var sub = subs.FirstOrDefault(s => string.Equals(s.SystemKey, source, StringComparison.OrdinalIgnoreCase));
        if (sub is null)
        {
            return Result<ResendShipmentArrivedResult>.Failure(
                $"Shipment-arrived callbacks for '{source}' are disabled (subscription off or not configured). Enable the subscription before resending.");
        }

        // Manual transport does not report arrival upstream (parity with the
        // auto ShipmentArrivedCallbackFanoutConsumer) — the source system owns
        // the arrival signal for operator-pool / self-managed deliveries.
        if (order.RequestedTransportMode == TransportMode.Manual)
        {
            return Result<ResendShipmentArrivedResult>.Failure(
                "Manual transport does not send arrival notifications to the source system.");
        }

        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<ResendShipmentArrivedResult>.Failure($"Trip {request.TripId} not found.");

        if (trip.DeliveryOrderId != request.OrderId)
        {
            return Result<ResendShipmentArrivedResult>.Failure(
                $"Trip {request.TripId} does not belong to order {request.OrderId}.");
        }

        var lots = order.Items
            .Where(i => i.TripId == request.TripId)
            .Select(i => i.ItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (lots.Count == 0)
        {
            return Result<ResendShipmentArrivedResult>.Failure(
                "No items are bound to this trip — nothing to send.");
        }

        // [Option A] Stable shipmentId across retry chain.
        var rootTripId = await _tripRepository.GetRootTripIdAsync(trip.Id, cancellationToken);
        var shipmentId = rootTripId.ToString();

        // Format via the SUBSCRIPTION's formatter and dispatch SYNCHRONOUSLY
        // so the operator sees the result immediately.
        var formatter = _formatterResolver.Resolve(sub.PayloadFormatKey);
        var context = new ShipmentArrivedContext(shipmentId, lots);
        var payload = await formatter.FormatAsync(context, cancellationToken);
        var msg = new OutboxMessage(
            id: Guid.NewGuid(),
            type: CallbackEventTypes.ShipmentArrivedV1,
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
                "[ShipmentArrivedResend] Trip {TripId} manual resend to {System} failed ({Status}): {Error}",
                trip.Id, source, status, ex.Message);
            return Result<ResendShipmentArrivedResult>.Failure(
                status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
                    ? $"{source} rejected the request ({(int)status}): {ex.Message}. Fix the data at upstream before resending."
                    : $"Callback to {source} failed: {ex.Message}");
        }
        sw.Stop();

        // F2 — the upstream has the callback; audit/activity are best-effort
        // from here (see the started handler).
        var auditDetails = $"trip-arrived shipmentId={shipmentId} attempt={trip.AttemptNumber} lots={lots.Count} latencyMs={sw.ElapsedMilliseconds}";
        try
        {
            await _auditRepository.AddAsync(new OrderAuditEvent(
                order.Id, UpstreamCallbackAudit.ArrivedManuallyResent, auditDetails,
                actorId: request.RequestedBy, systemKey: source),
                cancellationToken);
            await _auditRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "[ShipmentArrivedResend] Trip {TripId} resend DELIVERED to {System} but the audit write failed — timeline may miss it",
                trip.Id, source);
        }

        try
        {
            await _activityStore.AppendAsync(
                projectorName: UpstreamCallbackAudit.ProjectorName,
                eventId: Guid.NewGuid(),
                orderId: order.Id,
                category: UpstreamCallbackAudit.Category,
                eventType: UpstreamCallbackAudit.ArrivedManuallyResent,
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
                "[ShipmentArrivedResend] Trip {TripId} resend DELIVERED to {System} but the activity write failed — UI may not reflect it",
                trip.Id, source);
        }

        _logger.LogInformation(
            "[ShipmentArrivedResend] Trip {TripId} (attempt {N}) → {System} outcome=Success shipmentId={Sid} lots={LotCount} latencyMs={Ms} by={By}",
            trip.Id, trip.AttemptNumber, source, shipmentId, lots.Count, sw.ElapsedMilliseconds,
            request.RequestedBy ?? "(anonymous)");

        return Result<ResendShipmentArrivedResult>.Success(new ResendShipmentArrivedResult(
            ShipmentId: shipmentId,
            LotCount: lots.Count,
            LatencyMs: sw.ElapsedMilliseconds));
    }
}
