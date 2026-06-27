using DTMS.Planning.Application.Services;
using DTMS.Planning.Domain.Enums;
using DTMS.Planning.Domain.Repositories;
using DTMS.SharedKernel;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Planning.Application.Commands.RetryJob;

public class RetryJobCommandHandler : ICommandHandler<RetryJobCommand, RetryJobResult>
{
    private readonly IJobRepository _jobRepository;
    private readonly IDispatchOrderTemplateService _dispatch;
    private readonly ILogger<RetryJobCommandHandler> _logger;

    public RetryJobCommandHandler(
        IJobRepository jobRepository,
        IDispatchOrderTemplateService dispatch,
        ILogger<RetryJobCommandHandler> logger)
    {
        _jobRepository = jobRepository;
        _dispatch = dispatch;
        _logger = logger;
    }

    public async Task<Result<RetryJobResult>> Handle(RetryJobCommand request, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(request.JobId, cancellationToken);
        if (job is null)
            return Result<RetryJobResult>.Failure($"Job {request.JobId} not found.");

        if (!job.PickupStationId.HasValue || !job.DropStationId.HasValue)
            return Result<RetryJobResult>.Failure(
                $"Job {request.JobId} has no envelope route anchor — cannot retry an envelope dispatch.");

        Guid? previousTripId;
        int newAttempt;
        try
        {
            (previousTripId, newAttempt) = job.Retry();
        }
        catch (InvalidOperationException ex)
        {
            return Result<RetryJobResult>.Failure(ex.Message);
        }

        await _jobRepository.UpdateAsync(job, cancellationToken);

        var newUpperKey = EnvelopeUpperKey.Build(job.DeliveryOrderId, job.GroupIndex, newAttempt);
        _logger.LogInformation(
            "[RetryJob] Job {JobId} retry attempt {Attempt} (upperKey={UpperKey}, previousTrip={PrevTrip})",
            job.Id, newAttempt, newUpperKey, previousTripId?.ToString() ?? "(none)");

        var dispatchResult = await _dispatch.DispatchByRouteAsync(
            job.DeliveryOrderId,
            job.PickupStationId!.Value,
            job.DropStationId!.Value,
            newUpperKey,
            attemptNumber: newAttempt,
            previousAttemptId: previousTripId,
            jobId: job.Id,
            cancellationToken: cancellationToken);

        if (dispatchResult.IsSuccess && dispatchResult.Value.TripId != Guid.Empty)
        {
            job.MarkDispatched(dispatchResult.Value.TripId, dispatchResult.Value.VendorOrderKey);
            await _jobRepository.UpdateAsync(job, cancellationToken);
            return Result<RetryJobResult>.Success(new RetryJobResult(
                job.Id, newAttempt, Dispatched: true,
                dispatchResult.Value.TripId, dispatchResult.Value.VendorOrderKey, null));
        }

        // Two failure shapes here: dispatch IsSuccess but TripId empty
        // means RIOT3 accepted the order but we couldn't persist the Trip
        // (orphan — reconciliation required). Otherwise the vendor outright
        // rejected the dispatch (4xx/5xx — RIOT3-side decision).
        var reason = dispatchResult.IsSuccess
            ? $"vendor accepted (key={dispatchResult.Value.VendorOrderKey}) but trip persistence failed — reconciliation required"
            : (dispatchResult.Error ?? "dispatch failed");
        var category = dispatchResult.IsSuccess
            ? JobFailureCategory.TripPersistenceFailed
            : JobFailureCategory.VendorRejected;

        job.MarkFailed(reason, category);
        await _jobRepository.UpdateAsync(job, cancellationToken);

        return Result<RetryJobResult>.Success(new RetryJobResult(
            job.Id, newAttempt, Dispatched: false, null, null, reason));
    }
}
