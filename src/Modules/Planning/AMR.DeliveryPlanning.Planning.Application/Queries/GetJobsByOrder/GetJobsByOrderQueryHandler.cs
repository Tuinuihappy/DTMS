using AMR.DeliveryPlanning.Planning.Application.Queries.GetJobById;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetJobsByOrder;

public class GetJobsByOrderQueryHandler : IQueryHandler<GetJobsByOrderQuery, List<JobDto>>
{
    private readonly IJobRepository _jobRepository;

    public GetJobsByOrderQueryHandler(IJobRepository jobRepository) => _jobRepository = jobRepository;

    public async Task<Result<List<JobDto>>> Handle(GetJobsByOrderQuery request, CancellationToken cancellationToken)
    {
        var jobs = await _jobRepository.GetByDeliveryOrderIdAsync(request.OrderId, cancellationToken);
        return Result<List<JobDto>>.Success(jobs.Select(JobDto.From).ToList());
    }
}
