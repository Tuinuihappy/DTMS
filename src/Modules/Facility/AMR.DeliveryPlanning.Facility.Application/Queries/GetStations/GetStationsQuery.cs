using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Queries.GetStations;

public record StationDto(Guid Id, Guid MapId, string Name, StationType Type, double X, double Y, double? Theta,
    Guid? ZoneId, List<string> CompatibleVehicleTypes);

public record GetStationsQuery(Guid? MapId, StationType? Type, Guid? ZoneId, string? CompatibleVehicleType) : IQuery<List<StationDto>>;

public class GetStationsQueryHandler : IQueryHandler<GetStationsQuery, List<StationDto>>
{
    private readonly IStationRepository _repo;
    public GetStationsQueryHandler(IStationRepository repo) => _repo = repo;

    public async Task<Result<List<StationDto>>> Handle(GetStationsQuery request, CancellationToken cancellationToken)
    {
        var stations = await _repo.QueryAsync(request.MapId, request.Type, request.ZoneId, request.CompatibleVehicleType, cancellationToken);
        var dtos = stations.Select(s => new StationDto(
            s.Id, s.MapId, s.Name, s.Type,
            s.Coordinate.X, s.Coordinate.Y, s.Coordinate.Theta,
            s.ZoneId, s.CompatibleVehicleTypes)).ToList();
        return Result<List<StationDto>>.Success(dtos);
    }
}
