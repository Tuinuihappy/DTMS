using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Queries.GetMapById;

internal sealed class GetMapByIdQueryHandler : IQueryHandler<GetMapByIdQuery, MapDto>
{
    private readonly IMapRepository _mapRepository;

    public GetMapByIdQueryHandler(IMapRepository mapRepository)
    {
        _mapRepository = mapRepository;
    }

    public async Task<Result<MapDto>> Handle(GetMapByIdQuery request, CancellationToken cancellationToken)
    {
        var map = await _mapRepository.GetByIdAsync(request.MapId, cancellationToken);
        if (map is null)
        {
            throw new NotFoundException($"Map with ID {request.MapId} not found.");
        }

        var mapDto = new MapDto(
            map.Id,
            map.Name,
            map.Version,
            map.Width,
            map.Height,
            map.MapData);

        return Result<MapDto>.Success(mapDto);
    }
}
