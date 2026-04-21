using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CommitPlan;

public class CommitPlanCommandHandler : ICommandHandler<CommitPlanCommand>
{
    private readonly IJobRepository _jobRepository;

    public CommitPlanCommandHandler(IJobRepository jobRepository)
    {
        _jobRepository = jobRepository;
    }

    public async Task<Result> Handle(CommitPlanCommand request, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(request.JobId, cancellationToken);
        if (job == null)
            return Result.Failure($"Job {request.JobId} not found.");

        try
        {
            job.Commit();
            await _jobRepository.UpdateAsync(job, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
