using DTMS.Dispatch.IntegrationEvents;
using DTMS.Planning.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.Planning.Application.Consumers;

/// <summary>
/// Phase b9 — Mirror Trip lifecycle on the Job. TripCompleted is the
/// happy-path terminal: Dispatched|Executing → Completed. Idempotent.
/// </summary>
public class TripCompletedJobConsumer : IConsumer<TripCompletedIntegrationEvent>
{
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<TripCompletedJobConsumer> _logger;

    public TripCompletedJobConsumer(IJobRepository jobRepository, ILogger<TripCompletedJobConsumer> logger)
    {
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripCompletedIntegrationEvent> context)
    {
        var evt = context.Message;
        if (evt.JobId == Guid.Empty) return;

        var job = await _jobRepository.GetByIdAsync(evt.JobId, context.CancellationToken);
        if (job is null)
        {
            _logger.LogWarning("[JobSync] TripCompleted for unknown Job {JobId} (Trip {TripId})", evt.JobId, evt.TripId);
            return;
        }

        try
        {
            job.MarkCompleted(evt.TripId);
            await _jobRepository.UpdateAsync(job, context.CancellationToken);
            _logger.LogInformation("[JobSync] Job {JobId} → Completed (Trip {TripId})", job.Id, evt.TripId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[JobSync] TripCompleted ignored for Job {JobId}: {Err}", evt.JobId, ex.Message);
        }
    }
}
