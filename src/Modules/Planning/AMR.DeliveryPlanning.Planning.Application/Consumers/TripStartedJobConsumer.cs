using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Consumers;

/// <summary>
/// Phase b9 — Mirror Trip lifecycle on the Job aggregate. When RIOT3
/// reports the trip started executing, flip the Job from Dispatched →
/// Executing so the Planning view matches reality. Idempotent against
/// redelivery (Job.MarkExecuting is a no-op if already Executing).
///
/// Skips pre-Phase-b8 envelope trips (JobId = Guid.Empty) and jobs that
/// the operator already cancelled.
/// </summary>
public class TripStartedJobConsumer : IConsumer<TripStartedIntegrationEvent>
{
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<TripStartedJobConsumer> _logger;

    public TripStartedJobConsumer(IJobRepository jobRepository, ILogger<TripStartedJobConsumer> logger)
    {
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripStartedIntegrationEvent> context)
    {
        var evt = context.Message;
        if (evt.JobId == Guid.Empty) return;  // legacy envelope trip, no Job to update

        var job = await _jobRepository.GetByIdAsync(evt.JobId, context.CancellationToken);
        if (job is null)
        {
            _logger.LogWarning("[JobSync] TripStarted for unknown Job {JobId} (Trip {TripId})", evt.JobId, evt.TripId);
            return;
        }

        try
        {
            job.MarkExecuting(evt.TripId);
            await _jobRepository.UpdateAsync(job, context.CancellationToken);
            _logger.LogInformation("[JobSync] Job {JobId} → Executing (Trip {TripId})", job.Id, evt.TripId);
        }
        catch (InvalidOperationException ex)
        {
            // Status mismatch (e.g. Completed already) — log and swallow.
            // MassTransit retry would just hit the same wall.
            _logger.LogWarning("[JobSync] TripStarted ignored for Job {JobId}: {Err}", evt.JobId, ex.Message);
        }
    }
}
