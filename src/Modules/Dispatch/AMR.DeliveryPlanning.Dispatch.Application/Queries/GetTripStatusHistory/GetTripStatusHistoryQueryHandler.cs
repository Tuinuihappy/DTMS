using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Queries.GetTripStatusHistory;

public class GetTripStatusHistoryQueryHandler
    : IQueryHandler<GetTripStatusHistoryQuery, TripStatusHistoryResponse>
{
    private readonly ITripStatusHistoryReadRepository _repository;

    public GetTripStatusHistoryQueryHandler(ITripStatusHistoryReadRepository repository)
        => _repository = repository;

    public async Task<Result<TripStatusHistoryResponse>> Handle(
        GetTripStatusHistoryQuery request, CancellationToken cancellationToken)
    {
        var entries = await _repository.GetForTripAsync(request.TripId, cancellationToken);

        var dtos = entries
            .Select(e => new TripStatusHistoryEntryDto(
                e.EventId, e.TripId, e.DeliveryOrderId, e.JobId,
                e.FromStatus, e.ToStatus, e.OccurredAt, e.Reason))
            .ToList();

        var lastEventAt = dtos.Count > 0 ? dtos[0].OccurredAt : (DateTime?)null;

        return Result<TripStatusHistoryResponse>.Success(
            new TripStatusHistoryResponse(request.TripId, dtos, lastEventAt));
    }
}
