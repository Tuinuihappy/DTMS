using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Queries.GetTripItems;

public class GetTripItemsQueryHandler : IQueryHandler<GetTripItemsQuery, TripItemsResponse>
{
    private readonly ITripItemsReadRepository _repo;

    public GetTripItemsQueryHandler(ITripItemsReadRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<TripItemsResponse>> Handle(
        GetTripItemsQuery request, CancellationToken cancellationToken)
    {
        if (request.TripId == Guid.Empty)
            return Result<TripItemsResponse>.Failure("TripId is required.");

        var rows = await _repo.GetByTripAsync(request.TripId, cancellationToken);

        var items = rows
            .Select(r => new TripItemDto(
                ItemPk: r.ItemPk,
                LotNo: r.LotNo,
                ItemSeq: r.ItemSeq,
                ItemStatus: r.ItemStatus,
                PickupCode: r.PickupCode,
                DropCode: r.DropCode,
                WeightKg: r.WeightKg,
                Description: r.Description,
                Quantity: r.QuantityValue is { } qv && r.QuantityUom is { } qu
                    ? new TripItemQuantityDto(qv, qu)
                    : null,
                Order: new OrderRefDto(r.DeliveryOrderId, r.OrderRef, r.OrderStatus),
                BoundAt: r.BoundAt,
                LastEventAt: r.LastEventAt))
            .ToList();

        return Result<TripItemsResponse>.Success(
            new TripItemsResponse(request.TripId, items.Count, items));
    }
}
