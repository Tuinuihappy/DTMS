using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Mirror of <see cref="TripStartedOmsNotifyFaultConsumer"/> for the
/// arrived (drop completed) notification. Records a dead-letter audit
/// when the primary consumer exhausts retries.
/// </summary>
public class TripDropCompletedOmsNotifyFaultConsumer : IConsumer<Fault<TripDropCompletedIntegrationEvent>>
{
    private const string AuditEventType = "UpstreamOmsArrivedNotifyFailed";

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

        if (errorMessage.Length > 400) errorMessage = errorMessage[..400] + "…";

        var auditDetails = $"trip-arrived shipmentId={evt.TripId} failed: [{exceptionType}] {errorMessage}";
        try
        {
            await _auditRepository.AddAsync(new OrderAuditEvent(
                evt.DeliveryOrderId, AuditEventType, auditDetails),
                context.CancellationToken);
            await _auditRepository.SaveChangesAsync(context.CancellationToken);

            // P2.5 mirror so the OmsNotificationSection UI shows red "Failed".
            await _activityStore.AppendAsync(
                projectorName: "OmsNotifyDirect",
                eventId: Guid.NewGuid(),
                orderId: evt.DeliveryOrderId,
                category: "OmsNotify",
                eventType: AuditEventType,
                details: auditDetails,
                actorId: null,
                occurredAt: DateTime.UtcNow,
                relatedTripId: evt.TripId,
                attemptNumber: null,
                cancellationToken: context.CancellationToken);

            _logger.LogWarning(
                "[OmsArrived] Trip {TripId} dead-lettered after retries — audit recorded: {Error}",
                evt.TripId, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OmsArrived] Failed to record dead-letter audit for Trip {TripId}", evt.TripId);
        }
    }
}
