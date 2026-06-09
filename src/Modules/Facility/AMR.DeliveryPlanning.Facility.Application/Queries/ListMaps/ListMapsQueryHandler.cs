using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Queries.ListMaps;

internal sealed class ListMapsQueryHandler : IQueryHandler<ListMapsQuery, IReadOnlyList<MapSummaryDto>>
{
    private readonly IMapRepository _mapRepository;
    private readonly IStationRepository _stationRepository;

    public ListMapsQueryHandler(
        IMapRepository mapRepository,
        IStationRepository stationRepository)
    {
        _mapRepository = mapRepository;
        _stationRepository = stationRepository;
    }

    public async Task<Result<IReadOnlyList<MapSummaryDto>>> Handle(
        ListMapsQuery request, CancellationToken cancellationToken)
    {
        var maps = await _mapRepository.ListAsync(cancellationToken);

        var summaries = new List<MapSummaryDto>(maps.Count);
        foreach (var m in maps)
        {
            var stations = await _stationRepository.GetAllByMapAsync(m.Id, cancellationToken);
            summaries.Add(new MapSummaryDto(
                m.Id,
                m.Name,
                m.Version,
                m.Width,
                m.Height,
                m.VendorRef,
                stations.Count,
                stations.Count(s => s.IsActive)));
        }

        return Result<IReadOnlyList<MapSummaryDto>>.Success(summaries);
    }
}
