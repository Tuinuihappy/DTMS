using AMR.DeliveryPlanning.Planning.Application.Queries.GetJobById;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetJobsQueue;

public class GetJobsQueueQueryHandler : IQueryHandler<GetJobsQueueQuery, JobsQueueResult>
{
    private const int MaxPageSize = 200;

    private readonly IJobRepository _jobRepository;

    public GetJobsQueueQueryHandler(IJobRepository jobRepository) => _jobRepository = jobRepository;

    public async Task<Result<JobsQueueResult>> Handle(GetJobsQueueQuery request, CancellationToken cancellationToken)
    {
        if (request.Page < 1)
            return Result<JobsQueueResult>.Failure("Page must be >= 1.");
        if (request.PageSize is < 1 or > MaxPageSize)
            return Result<JobsQueueResult>.Failure($"PageSize must be between 1 and {MaxPageSize}.");

        var (jobs, total) = await _jobRepository.SearchQueueAsync(
            request.Statuses, request.Page, request.PageSize, cancellationToken);

        var items = jobs.Select(JobDto.From).ToList();
        return Result<JobsQueueResult>.Success(
            new JobsQueueResult(items, total, request.Page, request.PageSize));
    }
}
