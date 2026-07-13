using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Outbox;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Consumers;

/// <summary>
/// Phase S.5 — writes the per-order OMS-notification audit from a federated
/// callback's terminal outcome, replacing the direct writes the legacy
/// TripStarted/TripDropCompleted OMS-notify (+fault) consumers did. The
/// federated dispatch path only logs; this restores the rows the order-detail
/// "Upstream OMS notification" UI reads (filtered by <c>relatedTripId</c>,
/// classified by <c>eventType</c>, latest-wins by <c>occurredAt</c>).
///
/// <para>Only the OMS shipment.started / shipment.arrived outcomes are mirrored
/// — other callback event types (order.delivered/cancelled, other systems)
/// map to null and are ignored.</para>
/// </summary>
public sealed class SourceCallbackOutcomeConsumer : IConsumer<SourceCallbackOutcome>
{
    // Wire-contract slugs (mirror DTMS.Iam.Application.Callbacks.CallbackEventTypes;
    // hardcoded to avoid a cross-module reference for two stable strings).
    private const string ShipmentStartedV1 = "shipment.started.v1";
    private const string ShipmentArrivedV1 = "shipment.arrived.v1";

    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<SourceCallbackOutcomeConsumer> _logger;

    public SourceCallbackOutcomeConsumer(
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<SourceCallbackOutcomeConsumer> logger)
    {
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SourceCallbackOutcome> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        var auditType = MapAuditEventType(evt.CallbackEventType, evt.Success, evt.StatusCode);
        if (auditType is null)
            return;   // not an OMS shipment outcome we mirror to the UI

        var outcome = evt.Success ? "delivered" : "failed";
        var details =
            $"{evt.CallbackEventType} outcome={outcome} system={evt.SystemKey}" +
            (evt.StatusCode is { } sc ? $" status={sc}" : string.Empty) +
            (string.IsNullOrWhiteSpace(evt.Detail) ? string.Empty : $" detail={evt.Detail}");

        await _auditRepository.AddAsync(new OrderAuditEvent(evt.OrderId, auditType, details), ct);
        await _auditRepository.SaveChangesAsync(ct);

        // Mirror into the OrderActivity timeline the OmsNotificationSection UI
        // reads — same projectorName/category/relatedTripId the legacy direct
        // writes used, so the UI is byte-for-byte unaffected.
        await _activityStore.AppendAsync(
            projectorName: "OmsNotifyDirect",
            eventId: Guid.NewGuid(),
            orderId: evt.OrderId,
            category: "OmsNotify",
            eventType: auditType,
            details: details,
            actorId: null,
            occurredAt: DateTime.UtcNow,
            relatedTripId: evt.TripId,
            attemptNumber: null,
            cancellationToken: ct);

        _logger.LogInformation(
            "[SourceCallbackOutcome] Order {OrderId} trip {TripId} {EventType} → audit {AuditType}",
            evt.OrderId, evt.TripId, evt.CallbackEventType, auditType);
    }

    // Success/failure + 4xx-vs-other maps onto the exact audit types the UI's
    // notified/failed sets expect. 4xx = permanent (Rejected); 5xx/timeout/null
    // = transient failure after retries (Failed).
    private static string? MapAuditEventType(string callbackEventType, bool success, int? statusCode)
    {
        var permanent = statusCode is >= 400 and < 500;
        return callbackEventType switch
        {
            ShipmentStartedV1 => success ? "UpstreamOmsNotified"
                : permanent ? "UpstreamOmsRejected" : "UpstreamOmsNotifyFailed",
            ShipmentArrivedV1 => success ? "UpstreamOmsArrivedNotified"
                : permanent ? "UpstreamOmsArrivedRejected" : "UpstreamOmsArrivedNotifyFailed",
            _ => null,
        };
    }
}
