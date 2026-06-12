using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Consumers;

/// <summary>
/// Phase b9 — Vendor reported the trip failed. Flip the Job to Failed
/// with the vendor's reason text. Once Failed, the operator can hit
/// POST /api/v1/planning/jobs/{id}/retry to re-dispatch (Job.Retry()
/// resets to Created and bumps AttemptNumber).
/// </summary>
public class TripFailedJobConsumer : IConsumer<TripFailedIntegrationEvent>
{
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<TripFailedJobConsumer> _logger;

    public TripFailedJobConsumer(IJobRepository jobRepository, ILogger<TripFailedJobConsumer> logger)
    {
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripFailedIntegrationEvent> context)
    {
        var evt = context.Message;
        if (evt.JobId == Guid.Empty) return;

        var job = await _jobRepository.GetByIdAsync(evt.JobId, context.CancellationToken);
        if (job is null)
        {
            _logger.LogWarning("[JobSync] TripFailed for unknown Job {JobId} (Trip {TripId})", evt.JobId, evt.TripId);
            return;
        }

        try
        {
            job.MarkFailed($"vendor execution failed: {evt.Reason}");
            await _jobRepository.UpdateAsync(job, context.CancellationToken);
            _logger.LogInformation("[JobSync] Job {JobId} → Failed (Trip {TripId}, reason {Reason})",
                job.Id, evt.TripId, evt.Reason);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[JobSync] TripFailed ignored for Job {JobId}: {Err}", evt.JobId, ex.Message);
        }
    }
}
