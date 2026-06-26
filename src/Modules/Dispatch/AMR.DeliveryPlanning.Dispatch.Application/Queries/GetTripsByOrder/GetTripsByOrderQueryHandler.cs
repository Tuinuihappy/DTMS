using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Queries.GetTripsByOrder;

public class GetTripsByOrderQueryHandler : IQueryHandler<GetTripsByOrderQuery, List<TripSummaryDto>>
{
    private readonly ITripRepository _tripRepository;

    public GetTripsByOrderQueryHandler(ITripRepository tripRepository)
    {
        _tripRepository = tripRepository;
    }

    public async Task<Result<List<TripSummaryDto>>> Handle(GetTripsByOrderQuery request, CancellationToken cancellationToken)
    {
        var trips = await _tripRepository.GetByDeliveryOrderIdAsync(request.OrderId, cancellationToken);
        var dtos = trips
            .OrderBy(t => t.AttemptNumber)
            .ThenBy(t => t.CreatedAt)
            .Select(t => new TripSummaryDto(
                t.Id,
                t.DeliveryOrderId,
                t.JobId,
                t.Status.ToString(),
                t.UpperKey,
                t.VendorOrderKey,
                t.AttemptNumber,
                t.PreviousAttemptId,
                t.CreatedAt,
                t.StartedAt,
                t.CompletedAt))
            .ToList();
        return Result<List<TripSummaryDto>>.Success(dtos);
    }
}
