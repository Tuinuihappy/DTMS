using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Exceptions;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Mirror of <see cref="TripStartedOmsNotifyFaultConsumer"/> for the
/// arrived (drop completed) notification. Records a dead-letter audit
/// when the primary consumer exhausts retries.
///
/// Two audit shapes depending on the underlying exception:
///   - OmsPermanentException → UpstreamOmsArrivedRejected
///     (fast-failed 4xx data error — operator must fix data upstream)
///   - anything else         → UpstreamOmsArrivedNotifyFailed
///     (transient retries exhausted — OMS unreachable, can retry later)
/// </summary>
public class TripDropCompletedOmsNotifyFaultConsumer : IConsumer<Fault<TripDropCompletedIntegrationEvent>>
{
    private const string TransientAuditEventType = "UpstreamOmsArrivedNotifyFailed";
    private const string PermanentAuditEventType = "UpstreamOmsArrivedRejected";
    private static readonly string PermanentExceptionTypeName =
        typeof(OmsPermanentException).FullName!;

    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<TripDropCompletedOmsNotifyFaultConsumer> _logger;

    public TripDropCompletedOmsNotifyFaultConsumer(
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<TripDropCompletedOmsNotifyFaultConsumer> logger)
    {
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Fault<TripDropCompletedIntegrationEvent>> context)
    {
        var fault = context.Message;
        var evt = fault.Message;

        if (evt.DeliveryOrderId == Guid.Empty) return;

        var firstException = fault.Exceptions?.FirstOrDefault();
        var errorMessage = firstException?.Message ?? "(unknown error)";
        var exceptionType = firstException?.ExceptionType ?? "(unknown type)";
        var isPermanent = exceptionType == PermanentExceptionTypeName;

        if (errorMessage.Length > 400) errorMessage = errorMessage[..400] + "…";

        var auditEventType = isPermanent ? PermanentAuditEventType : TransientAuditEventType;
        var outcomeLabel = isPermanent ? "rejected" : "failed";
        var auditDetails = $"trip-arrived shipmentId={evt.TripId} {outcomeLabel}: [{exceptionType}] {errorMessage}";
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
                "[OmsArrived] Trip {TripId} {Outcome} — audit {AuditType} recorded: {Error}",
                evt.TripId, isPermanent ? "rejected by OMS (fast-fail)" : "dead-lettered after retries",
                auditEventType, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OmsArrived] Failed to record dead-letter audit for Trip {TripId}", evt.TripId);
        }
    }
}
