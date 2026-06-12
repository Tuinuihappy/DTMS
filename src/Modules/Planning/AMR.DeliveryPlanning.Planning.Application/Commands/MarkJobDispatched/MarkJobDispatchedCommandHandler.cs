using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
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
