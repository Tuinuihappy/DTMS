using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Phase OMS B4 — Dead-letter audit when
/// <see cref="PodCapturedOmsNotifyConsumer"/> exhausts retries.
///
/// PodCapturedIntegrationEvent lacks DeliveryOrderId, so the fault path
/// looks up Trip → DeliveryOrderId. If that lookup itself fails (e.g.
/// trip rolled back), the audit row is silently dropped — fault path
/// must never throw and create a Fault&lt;Fault&lt;T&gt;&gt; storm.
/// </summary>
public class PodCapturedOmsNotifyFaultConsumer : IConsumer<Fault<PodCapturedIntegrationEvent>>
{
    private const string AuditEventType = "UpstreamOmsPodCompletedNotifyFailed";

    private readonly ITripRepository _tripRepository;
    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly IOrderActivityProjectionStore _activityStore;
    private readonly ILogger<PodCapturedOmsNotifyFaultConsumer> _logger;

    public PodCapturedOmsNotifyFaultConsumer(
        ITripRepository tripRepository,
        IOrderAuditEventRepository auditRepository,
        IOrderActivityProjectionStore activityStore,
        ILogger<PodCapturedOmsNotifyFaultConsumer> logger)
    {
        _tripRepository = tripRepository;
        _auditRepository = auditRepository;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Fault<PodCapturedIntegrationEvent>> context)
    {
        var fault = context.Message;
        var evt = fault.Message;
        if (evt.TripId == Guid.Empty) return;

        try
        {
            var trip = await _tripRepository.GetByIdAsync(evt.TripId, context.CancellationToken);
            if (trip is null || trip.DeliveryOrderId == Guid.Empty)
            {
                _logger.LogWarning(
                    "[OmsPod] Fault for Trip {TripId} but trip / DeliveryOrderId not resolved — audit skipped",
                    evt.TripId);
                return;
            }

            var firstException = fault.Exceptions?.FirstOrDefault();
            var errorMessage = firstException?.Message ?? "(unknown error)";
            var exceptionType = firstException?.ExceptionType ?? "(unknown type)";
            if (errorMessage.Length > 400) errorMessage = errorMessage[..400] + "…";

            var auditDetails = $"pod-completed shipmentId={evt.TripId} failed: [{exceptionType}] {errorMessage}";
            await _auditRepository.AddAsync(new OrderAuditEvent(
                trip.DeliveryOrderId, AuditEventType, auditDetails),
                context.CancellationToken);
            await _auditRepository.SaveChangesAsync(context.CancellationToken);

            await _activityStore.AppendAsync(
                projectorName: "OmsNotifyDirect",
                eventId: Guid.NewGuid(),
                orderId: trip.DeliveryOrderId,
                category: "OmsNotify",
                eventType: AuditEventType,
                details: auditDetails,
                actorId: null,
                occurredAt: DateTime.UtcNow,
                relatedTripId: evt.TripId,
                attemptNumber: null,
                cancellationToken: context.CancellationToken);

            _logger.LogWarning(
                "[OmsPod] Trip {TripId} dead-lettered after retries — audit recorded: {Error}",
                evt.TripId, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OmsPod] Failed to record dead-letter audit for Trip {TripId}", evt.TripId);
        }
    }
}
