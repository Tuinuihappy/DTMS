using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Phase OMS B4 — Records a dead-letter audit when
/// <see cref="TripFailedOmsNotifyConsumer"/> exhausts its retry policy.
/// Mirrors <see cref="TripStartedOmsNotifyFaultConsumer"/>.
/// </summary>
public class TripFailedOmsNotifyFaultConsumer : IConsumer<Fault<TripFailedIntegrationEvent>>
{
    private const string AuditEventType = "UpstreamOmsTripFailedNotifyFailed";

    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<TripFailedOmsNotifyFaultConsumer> _logger;

    public TripFailedOmsNotifyFaultConsumer(
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<TripFailedOmsNotifyFaultConsumer> logger)
    {
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Fault<TripFailedIntegrationEvent>> context)
    {
        var fault = context.Message;
        var evt = fault.Message;
        if (evt.DeliveryOrderId == Guid.Empty) return;

        var firstException = fault.Exceptions?.FirstOrDefault();
        var errorMessage = firstException?.Message ?? "(unknown error)";
        var exceptionType = firstException?.ExceptionType ?? "(unknown type)";
        if (errorMessage.Length > 400) errorMessage = errorMessage[..400] + "…";

        var auditDetails = $"trip-failed shipmentId={evt.TripId} failed: [{exceptionType}] {errorMessage}";
        try
        {
            await _auditRepository.AddAsync(new OrderAuditEvent(
                evt.DeliveryOrderId, AuditEventType, auditDetails),
                context.CancellationToken);
            await _auditRepository.SaveChangesAsync(context.CancellationToken);

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
                "[OmsFailed] Trip {TripId} dead-lettered after retries — audit recorded: {Error}",
                evt.TripId, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OmsFailed] Failed to record dead-letter audit for Trip {TripId}", evt.TripId);
        }
    }
}
