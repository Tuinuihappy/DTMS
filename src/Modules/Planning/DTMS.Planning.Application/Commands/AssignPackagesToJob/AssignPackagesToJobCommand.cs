using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.AssignPackagesToJob;

public record AssignPackagesToJobCommand(Guid JobId, List<string> PackageBarcodes) : ICommand<bool>;

public class AssignPackagesToJobCommandHandler : ICommandHandler<AssignPackagesToJobCommand, bool>
{
    private readonly IJobRepository _jobRepository;

    public AssignPackagesToJobCommandHandler(IJobRepository jobRepository)
        => _jobRepository = jobRepository;

    public async Task<Result<bool>> Handle(AssignPackagesToJobCommand request, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(request.JobId, cancellationToken);
        if (job is null)
            return Result<bool>.Failure($"Job {request.JobId} not found.");

        job.SetPackageBarcodes(request.PackageBarcodes);
        await _jobRepository.UpdateAsync(job, cancellationToken);
        return Result<bool>.Success(true);
    }
}
