using DTMS.Dispatch.IntegrationEvents;
using DTMS.Planning.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.Planning.Application.Consumers;

/// <summary>
/// Phase #1 — Mirror of TripPausedJobConsumer for the resume side.
/// Trip resumed → flip Job from Paused back to Executing. Idempotent
/// via Job.MarkResumed's state guards.
/// </summary>
public class TripResumedJobConsumer : IConsumer<TripResumedIntegrationEventV1>
{
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<TripResumedJobConsumer> _logger;

    public TripResumedJobConsumer(IJobRepository jobRepository, ILogger<TripResumedJobConsumer> logger)
    {
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripResumedIntegrationEventV1> context)
    {
        var evt = context.Message;
        var job = await _jobRepository.GetByTripIdAsync(evt.TripId, context.CancellationToken);
        if (job is null)
        {
            _logger.LogDebug("[JobSync] TripResumed for Trip {TripId} — no linked Job", evt.TripId);
            return;
        }

        job.MarkResumed(evt.TripId);
        await _jobRepository.UpdateAsync(job, context.CancellationToken);
        _logger.LogInformation("[JobSync] Job {JobId} → Executing (resumed from Trip {TripId})",
            job.Id, evt.TripId);
    }
}
