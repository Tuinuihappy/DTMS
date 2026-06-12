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
    private readonly ILogger<TripDropCompletedOmsNotifyFaultConsumer> _logger;

    public TripDropCompletedOmsNotifyFaultConsumer(
        IOrderAuditEventRepository auditRepository,
        ILogger<TripDropCompletedOmsNotifyFaultConsumer> logger)
    {
        _auditRepository = auditRepository;
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

        try
        {
            await _auditRepository.AddAsync(new OrderAuditEvent(
                evt.DeliveryOrderId,
                AuditEventType,
                $"trip-arrived shipmentId={evt.TripId} failed: [{exceptionType}] {errorMessage}"),
                context.CancellationToken);
            await _auditRepository.SaveChangesAsync(context.CancellationToken);

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
