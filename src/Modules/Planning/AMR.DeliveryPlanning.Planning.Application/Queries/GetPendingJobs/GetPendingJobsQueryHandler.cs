using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetPendingJobs;

public class GetPendingJobsQueryHandler : IQueryHandler<GetPendingJobsQuery, List<Job>>
{
    private readonly IJobRepository _jobRepository;

    public GetPendingJobsQueryHandler(IJobRepository jobRepository)
    {
        _jobRepository = jobRepository;
    }

    public async Task<Result<List<Job>>> Handle(GetPendingJobsQuery request, CancellationToken cancellationToken)
    {
        var jobs = await _jobRepository.GetPendingJobsAsync(cancellationToken);
        return Result<List<Job>>.Success(jobs);
    }
}
