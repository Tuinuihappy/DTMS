using DTMS.Dispatch.IntegrationEvents;
using DTMS.Planning.Domain.Enums;
using DTMS.Planning.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.Planning.Application.Consumers;

/// <summary>
/// Phase b9 — Vendor reported the trip failed. Flip the Job to Failed
/// with the vendor's reason text. Once Failed, the operator can hit
/// POST /api/v1/planning/jobs/{id}/retry to re-dispatch (Job.Retry()
/// resets to Created and bumps AttemptNumber).
/// TripRejected lands on the same Job outcome (VendorExecutionFailed) —
/// only the reason prefix distinguishes the two on the Job record.
/// </summary>
public class TripFailedJobConsumer :
    IConsumer<TripFailedIntegrationEvent>,
    IConsumer<TripRejectedIntegrationEventV1>
{
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<TripFailedJobConsumer> _logger;

    public TripFailedJobConsumer(IJobRepository jobRepository, ILogger<TripFailedJobConsumer> logger)
    {
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> context)
        => HandleAsync(context, context.Message.JobId, context.Message.TripId,
            $"vendor execution failed: {context.Message.Reason}", eventName: "TripFailed");

    public Task Consume(ConsumeContext<TripRejectedIntegrationEventV1> context)
        => HandleAsync(context, context.Message.JobId, context.Message.TripId,
            $"vendor rejected: {context.Message.Reason}", eventName: "TripRejected");

    private async Task HandleAsync(
        ConsumeContext context, Guid jobId, Guid tripId, string reason, string eventName)
    {
        if (jobId == Guid.Empty) return;

        var job = await _jobRepository.GetByIdAsync(jobId, context.CancellationToken);
        if (job is null)
        {
            _logger.LogWarning("[JobSync] {EventName} for unknown Job {JobId} (Trip {TripId})", eventName, jobId, tripId);
            return;
        }

        try
        {
            job.MarkFailed(reason, JobFailureCategory.VendorExecutionFailed);
            await _jobRepository.UpdateAsync(job, context.CancellationToken);
            _logger.LogInformation("[JobSync] Job {JobId} → Failed (Trip {TripId}, reason {Reason})",
                job.Id, tripId, reason);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[JobSync] {EventName} ignored for Job {JobId}: {Err}", eventName, jobId, ex.Message);
        }
    }
}
