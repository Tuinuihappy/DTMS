using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Queries.GetTripItems;

public class GetTripItemsQueryHandler : IQueryHandler<GetTripItemsQuery, TripItemsResponse>
{
    private readonly ITripItemsReadRepository _repo;
    private readonly ITripRepository _tripRepository;

    public GetTripItemsQueryHandler(
        ITripItemsReadRepository repo,
        ITripRepository tripRepository)
    {
        _repo = repo;
        _tripRepository = tripRepository;
    }

    public async Task<Result<TripItemsResponse>> Handle(
        GetTripItemsQuery request, CancellationToken cancellationToken)
    {
        if (request.TripId == Guid.Empty)
            return Result<TripItemsResponse>.Failure("TripId is required.");

        // BFF — sequential reads on the shared DispatchDbContext (EF Core
        // forbids concurrent operations on the same context instance).
        // Both queries hit the same connection so wall-clock cost is one
        // round-trip per query; Postgres latency-dominated, not chained-await.
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<TripItemsResponse>.Failure($"Trip {request.TripId} not found.");

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
                Order: new OrderRefDto(r.DeliveryOrderId, r.OrderRef, r.OrderStatus, r.OrderTransportMode),
                BoundAt: r.BoundAt,
                LastEventAt: r.LastEventAt))
            .ToList();

        var tripContext = new TripContextDto(
            Status: trip.Status.ToString(),
            AttemptNumber: trip.AttemptNumber,
            UpperKey: trip.UpperKey,
            VendorOrderKey: trip.VendorOrderKey,
            VendorVehicleKey: trip.VendorVehicleKey,
            VendorVehicleName: trip.VendorVehicleName,
            TemplateNameAtDispatch: trip.TemplateNameAtDispatch,
            PriorityAtDispatch: trip.PriorityAtDispatch,
            CreatedAt: trip.CreatedAt,
            StartedAt: trip.StartedAt,
            CompletedAt: trip.CompletedAt,
            FailureReason: trip.FailureReason);

        return Result<TripItemsResponse>.Success(
            new TripItemsResponse(request.TripId, tripContext, items));
    }
}
