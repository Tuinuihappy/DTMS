using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Api.Adapters;

/// <summary>
/// Composition-root bridge: lets Dispatch.Application's
/// ReissueTripCommand reach Planning.Application's
/// IDispatchOrderTemplateService without Dispatch.Application taking a
/// direct dependency on Planning.Application. Phase b9 — also handles
/// the Job-side state reset when a Trip-level retry preserves a JobId.
/// </summary>
internal sealed class PlanningTripRetryDispatcher : ITripRetryDispatcher
{
    private readonly IDispatchOrderTemplateService _dispatchService;
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<PlanningTripRetryDispatcher> _logger;

    public PlanningTripRetryDispatcher(
        IDispatchOrderTemplateService dispatchService,
        IJobRepository jobRepository,
        ILogger<PlanningTripRetryDispatcher> logger)
    {
        _dispatchService = dispatchService;
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public async Task<Result<Guid>> ReissueAsync(
        Guid deliveryOrderId,
        Guid pickupStationId,
        Guid dropStationId,
        string newUpperKey,
        int attemptNumber,
        Guid previousAttemptId,
        Guid? jobId,
        CancellationToken cancellationToken = default)
    {
        var result = await _dispatchService.DispatchByRouteAsync(
            deliveryOrderId,
            pickupStationId,
            dropStationId,
            newUpperKey,
            attemptNumber: attemptNumber,
            previousAttemptId: previousAttemptId,
            jobId: jobId,
            cancellationToken: cancellationToken);

        if (result.IsFailure)
            return Result<Guid>.Failure(result.Error!);

        var newTripId = result.Value.TripId;

        // Phase b9 — if a Job was bound to the original Trip, rebind it
        // to the new one so the upcoming TripStarted webhook can flip Job
        // back into Executing rather than throwing on a stale state.
        // Best-effort: a failure here logs but doesn't fail the retry —
        // the new Trip still exists and serves the customer.
        if (jobId is { } id && newTripId != Guid.Empty)
        {
            try
            {
                var job = await _jobRepository.GetByIdAsync(id, cancellationToken);
                if (job is not null)
                {
                    job.RebindToRetryTrip(newTripId, result.Value.VendorOrderKey);
                    await _jobRepository.UpdateAsync(job, cancellationToken);
                    _logger.LogInformation(
                        "[TripRetry] Job {JobId} rebound to retry Trip {NewTripId} (attempt {Attempt})",
                        id, newTripId, job.AttemptNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[TripRetry] Job {JobId} rebind failed after Trip {NewTripId} dispatched — job state may drift",
                    id, newTripId);
            }
        }

        return Result<Guid>.Success(newTripId);
    }
}
