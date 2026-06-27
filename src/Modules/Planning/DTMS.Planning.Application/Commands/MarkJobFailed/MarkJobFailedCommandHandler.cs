using DTMS.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Planning.Application.Commands.MarkJobFailed;

public class MarkJobFailedCommandHandler : ICommandHandler<MarkJobFailedCommand>
{
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<MarkJobFailedCommandHandler> _logger;

    public MarkJobFailedCommandHandler(
        IJobRepository jobRepository,
        ILogger<MarkJobFailedCommandHandler> logger)
    {
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkJobFailedCommand request, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(request.JobId, cancellationToken);
        if (job is null) return Result.Failure($"Job {request.JobId} not found.");

        try
        {
            job.MarkFailed(request.Reason, request.Category);
            await _jobRepository.UpdateAsync(job, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[MarkJobFailed] {JobId}: {Error}", request.JobId, ex.Message);
            return Result.Failure(ex.Message);
        }
    }
}
