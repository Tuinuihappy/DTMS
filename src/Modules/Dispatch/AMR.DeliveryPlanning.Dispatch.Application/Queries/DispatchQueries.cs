using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Queries.GetTripById;

public record GetTripByIdQuery(Guid TripId) : IQuery<Trip>;

public class GetTripByIdQueryHandler : IQueryHandler<GetTripByIdQuery, Trip>
{
    private readonly ITripRepository _tripRepository;
    public GetTripByIdQueryHandler(ITripRepository tripRepository) => _tripRepository = tripRepository;

    public async Task<Result<Trip>> Handle(GetTripByIdQuery request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        return trip != null ? Result<Trip>.Success(trip) : Result<Trip>.Failure($"Trip {request.TripId} not found.");
    }
}

public record GetActiveTripsByVehicleQuery(Guid VehicleId) : IQuery<List<Trip>>;

public class GetActiveTripsByVehicleQueryHandler : IQueryHandler<GetActiveTripsByVehicleQuery, List<Trip>>
{
    private readonly ITripRepository _tripRepository;
    public GetActiveTripsByVehicleQueryHandler(ITripRepository tripRepository) => _tripRepository = tripRepository;

    public async Task<Result<List<Trip>>> Handle(GetActiveTripsByVehicleQuery request, CancellationToken cancellationToken)
    {
        var trips = await _tripRepository.GetActiveTripsByVehicleAsync(request.VehicleId, cancellationToken);
        return Result<List<Trip>>.Success(trips);
    }
}
