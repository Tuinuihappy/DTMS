using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Records a dead-letter audit when <see cref="TripStartedOmsNotifyConsumer"/>
/// exhausts its retry policy. MassTransit fans out a Fault&lt;T&gt; message
/// after the last retry attempt fails — this is our hook for surfacing the
/// failure into the order timeline so operators can see "OMS never accepted
/// this shipment" without grepping logs.
///
/// Independent of the main consumer's lifecycle: writes audit-only, never
/// throws (a failure here just loses the audit row, not the original event).
/// </summary>
public class TripStartedOmsNotifyFaultConsumer : IConsumer<Fault<TripStartedIntegrationEvent>>
{
    private const string AuditEventType = "UpstreamOmsNotifyFailed";

    private readonly IOrderAuditEventRepository _auditRepository;
    private readonly ILogger<TripStartedOmsNotifyFaultConsumer> _logger;

    public TripStartedOmsNotifyFaultConsumer(
        IOrderAuditEventRepository auditRepository,
        ILogger<TripStartedOmsNotifyFaultConsumer> logger)
    {
        _auditRepository = auditRepository;
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

        // Truncate to keep the audit detail row bounded; full stack lives
        // in MassTransit's _error queue + structured log.
        if (errorMessage.Length > 400) errorMessage = errorMessage[..400] + "…";

        try
        {
            await _auditRepository.AddAsync(new OrderAuditEvent(
                evt.DeliveryOrderId,
                AuditEventType,
                $"trip-started shipmentId={evt.TripId} failed: [{exceptionType}] {errorMessage}"),
                context.CancellationToken);
            await _auditRepository.SaveChangesAsync(context.CancellationToken);

            _logger.LogWarning(
                "[OmsNotify] Trip {TripId} dead-lettered after retries — audit recorded: {Error}",
                evt.TripId, errorMessage);
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
