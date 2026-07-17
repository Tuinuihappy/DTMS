using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Outbox;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Consumers;

/// <summary>
/// Phase S.5 — writes the per-order upstream-notification audit from a
/// federated callback's terminal outcome. The federated dispatch path only
/// logs; this produces the rows the order-detail upstream-notification UI
/// reads (filtered by <c>relatedTripId</c>, classified by <c>eventType</c>,
/// latest-wins by <c>occurredAt</c>).
///
/// <para>Phase C — system-neutral: the labels come from
/// <see cref="UpstreamCallbackAudit"/> and the system lands in the
/// <c>SystemKey</c> column, so a sap/erp outcome writes the same vocabulary
/// tagged with its own key instead of masquerading as OMS. The shipment.*
/// family is the ENTIRE callback vocabulary (the order-scoped pair was
/// removed 2026-07-17 — never subscribed, never audited-able); the null arm
/// below is a defensive guard for unknown future types, and adding a type
/// without a label here would strand its outcomes silently — extend the
/// switch in the same commit.</para>
/// </summary>
public sealed class SourceCallbackOutcomeConsumer : IConsumer<SourceCallbackOutcome>
{
    // Wire-contract slugs (mirror DTMS.Iam.Application.Callbacks.CallbackEventTypes;
    // hardcoded to avoid a cross-module reference for a few stable strings).
    private const string ShipmentStartedV1 = "shipment.started.v1";
    private const string ShipmentArrivedV1 = "shipment.arrived.v1";
    private const string ShipmentCancelledV1 = "shipment.cancelled.v1";

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

        await _auditRepository.AddAsync(
            new OrderAuditEvent(evt.OrderId, auditType, details, systemKey: evt.SystemKey), ct);
        await _auditRepository.SaveChangesAsync(ct);

        // Mirror into the OrderActivity timeline the upstream-notification UI
        // reads — same relatedTripId semantics the legacy direct writes used.
        await _activityStore.AppendAsync(
            projectorName: UpstreamCallbackAudit.ProjectorName,
            eventId: Guid.NewGuid(),
            orderId: evt.OrderId,
            category: UpstreamCallbackAudit.Category,
            eventType: auditType,
            details: details,
            actorId: null,
            occurredAt: DateTime.UtcNow,
            relatedTripId: evt.TripId,
            attemptNumber: null,
            cancellationToken: ct,
            systemKey: evt.SystemKey);

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
            ShipmentStartedV1 => success ? UpstreamCallbackAudit.Notified
                : permanent ? UpstreamCallbackAudit.Rejected : UpstreamCallbackAudit.NotifyFailed,
            ShipmentArrivedV1 => success ? UpstreamCallbackAudit.ArrivedNotified
                : permanent ? UpstreamCallbackAudit.ArrivedRejected : UpstreamCallbackAudit.ArrivedNotifyFailed,
            ShipmentCancelledV1 => success ? UpstreamCallbackAudit.CancelledNotified
                : permanent ? UpstreamCallbackAudit.CancelledRejected : UpstreamCallbackAudit.CancelledNotifyFailed,
            _ => null,
        };
    }
}
