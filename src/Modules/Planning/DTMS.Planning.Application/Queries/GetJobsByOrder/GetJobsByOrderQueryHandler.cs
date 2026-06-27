using DTMS.Planning.Application.Queries.GetJobById;
using DTMS.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Queries.GetJobsByOrder;

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
