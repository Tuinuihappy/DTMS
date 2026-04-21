using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetJobById;

public class GetJobByIdQueryHandler : IQueryHandler<GetJobByIdQuery, Job>
{
    private readonly IJobRepository _jobRepository;

    public GetJobByIdQueryHandler(IJobRepository jobRepository)
    {
        _jobRepository = jobRepository;
    }

    public async Task<Result<Job>> Handle(GetJobByIdQuery request, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(request.JobId, cancellationToken);
        if (job == null)
            return Result<Job>.Failure($"Job {request.JobId} not found.");

        return Result<Job>.Success(job);
    }
}
