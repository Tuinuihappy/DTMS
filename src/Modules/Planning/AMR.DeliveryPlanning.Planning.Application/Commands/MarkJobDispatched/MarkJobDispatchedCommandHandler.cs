using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.MarkJobDispatched;

public class MarkJobDispatchedCommandHandler : ICommandHandler<MarkJobDispatchedCommand>
{
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<MarkJobDispatchedCommandHandler> _logger;

    public MarkJobDispatchedCommandHandler(
        IJobRepository jobRepository,
        ILogger<MarkJobDispatchedCommandHandler> logger)
    {
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkJobDispatchedCommand request, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(request.JobId, cancellationToken);
        if (job is null) return Result.Failure($"Job {request.JobId} not found.");

        // T1.5 — idempotent on (JobId, TripId). When MassTransit redelivers
        // the consumer (T1.1) the job may already be Dispatched from the prior
        // attempt; succeed silently if state matches. Mismatched TripId means
        // the second attempt produced a *different* Trip — log loudly and fail
        // so the divergence is investigated rather than papered over.
        if (job.Status == Domain.Enums.JobStatus.Dispatched)
        {
            if (job.TripId == request.TripId)
            {
                _logger.LogInformation(
                    "[MarkJobDispatched] ↺ Job {JobId} already Dispatched with same TripId {TripId} — no-op (idempotent)",
                    request.JobId, request.TripId);
                return Result.Success();
            }

            _logger.LogError(
                "[MarkJobDispatched] ✗ Job {JobId} already Dispatched with different TripId existing={Existing} attempted={Attempted}",
                request.JobId, job.TripId, request.TripId);
            return Result.Failure(
                $"Job {request.JobId} already dispatched as trip {job.TripId}, cannot rebind to {request.TripId}.");
        }

        try
        {
            job.MarkDispatched(request.TripId, request.VendorOrderKey);
            await _jobRepository.UpdateAsync(job, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[MarkJobDispatched] {JobId}: {Error}", request.JobId, ex.Message);
            return Result.Failure(ex.Message);
        }
    }
}
