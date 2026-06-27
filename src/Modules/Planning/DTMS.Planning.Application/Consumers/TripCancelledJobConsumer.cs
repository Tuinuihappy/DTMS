using DTMS.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Consumers;

/// <summary>
/// Phase b9 — Vendor or operator cancelled the trip. Job is set to
/// Cancelled (terminal). Job.Retry() does NOT work from Cancelled —
/// the operator's intent was to abandon, not re-attempt. To re-dispatch
/// the same route, a new DeliveryOrder confirmation is required.
/// </summary>
public class TripCancelledJobConsumer : IConsumer<TripCancelledIntegrationEvent>
{
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<TripCancelledJobConsumer> _logger;

    public TripCancelledJobConsumer(IJobRepository jobRepository, ILogger<TripCancelledJobConsumer> logger)
    {
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripCancelledIntegrationEvent> context)
    {
        var evt = context.Message;
        if (evt.JobId == Guid.Empty) return;

        var job = await _jobRepository.GetByIdAsync(evt.JobId, context.CancellationToken);
        if (job is null)
        {
            _logger.LogWarning("[JobSync] TripCancelled for unknown Job {JobId} (Trip {TripId})", evt.JobId, evt.TripId);
            return;
        }

        try
        {
            job.MarkCancelled(evt.TripId, evt.Reason);
            await _jobRepository.UpdateAsync(job, context.CancellationToken);
            _logger.LogInformation("[JobSync] Job {JobId} → Cancelled (Trip {TripId}, reason {Reason})",
                job.Id, evt.TripId, evt.Reason);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[JobSync] TripCancelled ignored for Job {JobId}: {Err}", evt.JobId, ex.Message);
        }
    }
}
