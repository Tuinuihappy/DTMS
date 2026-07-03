using DTMS.SharedKernel.Messaging;
using DTMS.Wms.Domain.Repositories;

namespace DTMS.Wms.Application.Queries.GetWmsLocations;

internal sealed class GetWmsLocationsQueryHandler
    : IQueryHandler<GetWmsLocationsQuery, GetWmsLocationsResult>
{
    private readonly IWmsLocationRepository _repo;

    public GetWmsLocationsQueryHandler(IWmsLocationRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<GetWmsLocationsResult>> Handle(
        GetWmsLocationsQuery request, CancellationToken ct)
    {
        var (items, total) = await _repo.QueryAsync(
            request.Search,
            request.ParentCode,
            request.Page,
            request.PageSize,
            request.IncludeInactive,
            ct);

        var data = items.Select(l => new WmsLocationSummaryDto(
            l.Id,
            l.ExternalId,
            l.LocationCode,
            l.DisplayName,
            l.Type,
            l.TypeName,
            l.IsActive,
            l.ParentLocationCode,
            l.ParentLocationDisplayName,
            l.Latitude,
            l.Longitude)).ToList();

        return Result<GetWmsLocationsResult>.Success(
            new GetWmsLocationsResult(total, request.Page, request.PageSize, data));
    }
}
