using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.OmsAdapter.Abstractions.Exceptions;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Consumers;

/// <summary>
/// Records a dead-letter audit when <see cref="TripStartedOmsNotifyConsumer"/>
/// exhausts its retry policy. MassTransit fans out a Fault&lt;T&gt; message
/// after the last retry attempt fails — this is our hook for surfacing the
/// failure into the order timeline so operators can see "OMS never accepted
/// this shipment" without grepping logs.
///
/// Two audit shapes depending on the underlying exception:
///   - OmsPermanentException → UpstreamOmsRejected
///     (fast-failed 4xx data error — operator must fix data upstream)
///   - anything else         → UpstreamOmsNotifyFailed
///     (transient retries exhausted — OMS unreachable, can retry later)
///
/// Independent of the main consumer's lifecycle: writes audit-only, never
/// throws (a failure here just loses the audit row, not the original event).
/// </summary>
public class TripStartedOmsNotifyFaultConsumer : IConsumer<Fault<TripStartedIntegrationEvent>>
{
    private const string TransientAuditEventType = "UpstreamOmsNotifyFailed";
    private const string PermanentAuditEventType = "UpstreamOmsRejected";
    private static readonly string PermanentExceptionTypeName =
        typeof(OmsPermanentException).FullName!;

    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<TripStartedOmsNotifyFaultConsumer> _logger;

    public TripStartedOmsNotifyFaultConsumer(
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<TripStartedOmsNotifyFaultConsumer> logger)
    {
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Fault<TripStartedIntegrationEvent>> context)
    {
        var fault = context.Message;
        var evt = fault.Message;

        if (evt.DeliveryOrderId == Guid.Empty) return;

        var firstException = fault.Exceptions?.FirstOrDefault();
        var errorMessage = firstException?.Message ?? "(unknown error)";
        var exceptionType = firstException?.ExceptionType ?? "(unknown type)";
        var isPermanent = exceptionType == PermanentExceptionTypeName;

        // Truncate to keep the audit detail row bounded; full stack lives
        // in MassTransit's _error queue + structured log.
        if (errorMessage.Length > 400) errorMessage = errorMessage[..400] + "…";

        var auditEventType = isPermanent ? PermanentAuditEventType : TransientAuditEventType;
        var outcomeLabel = isPermanent ? "rejected" : "failed";
        var auditDetails = $"trip-started shipmentId={evt.TripId} {outcomeLabel}: [{exceptionType}] {errorMessage}";
        try
        {
            await _auditRepository.AddAsync(new OrderAuditEvent(
                evt.DeliveryOrderId, auditEventType, auditDetails),
                context.CancellationToken);
            await _auditRepository.SaveChangesAsync(context.CancellationToken);

            // P2.5 mirror so the OmsNotificationSection UI shows red "Failed".
            await _activityStore.AppendAsync(
                projectorName: "OmsNotifyDirect",
                eventId: Guid.NewGuid(),
                orderId: evt.DeliveryOrderId,
                category: "OmsNotify",
                eventType: auditEventType,
                details: auditDetails,
                actorId: null,
                occurredAt: DateTime.UtcNow,
                relatedTripId: evt.TripId,
                attemptNumber: null,
                cancellationToken: context.CancellationToken);

            _logger.LogWarning(
                "[OmsNotify] Trip {TripId} {Outcome} — audit {AuditType} recorded: {Error}",
                evt.TripId, isPermanent ? "rejected by OMS (fast-fail)" : "dead-lettered after retries",
                auditEventType, errorMessage);
        }
        catch (Exception ex)
        {
            // Never throw from the fault path — that would just fan out
            // another Fault<Fault<T>>. Lost audit is annoying but recoverable
            // (the original message still sits in _error queue for ops).
            _logger.LogError(ex,
                "[OmsNotify] Failed to record dead-letter audit for Trip {TripId}", evt.TripId);
        }
    }
}
