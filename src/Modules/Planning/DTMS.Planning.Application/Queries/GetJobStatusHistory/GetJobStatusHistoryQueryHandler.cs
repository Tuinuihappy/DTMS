using DTMS.Planning.Application.Projections;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Queries.GetJobStatusHistory;

public class GetJobStatusHistoryQueryHandler
    : IQueryHandler<GetJobStatusHistoryQuery, JobStatusHistoryResponse>
{
    private readonly IJobStatusHistoryReadRepository _repository;

    public GetJobStatusHistoryQueryHandler(IJobStatusHistoryReadRepository repository)
        => _repository = repository;

    public async Task<Result<JobStatusHistoryResponse>> Handle(
        GetJobStatusHistoryQuery request, CancellationToken cancellationToken)
    {
        var entries = await _repository.GetForJobAsync(request.JobId, cancellationToken);

        var dtos = entries
            .Select(e => new JobStatusHistoryEntryDto(
                e.EventId, e.JobId, e.DeliveryOrderId,
                e.FromStatus, e.ToStatus, e.OccurredAt, e.Reason))
            .ToList();

        var lastEventAt = dtos.Count > 0 ? dtos[0].OccurredAt : (DateTime?)null;

        return Result<JobStatusHistoryResponse>.Success(
            new JobStatusHistoryResponse(request.JobId, dtos, lastEventAt));
    }
}
