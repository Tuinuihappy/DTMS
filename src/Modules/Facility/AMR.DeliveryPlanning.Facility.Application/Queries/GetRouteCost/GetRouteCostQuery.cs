using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Caching.Distributed;

namespace AMR.DeliveryPlanning.Facility.Application.Queries.GetRouteCost;

public record RouteCostDto(Guid FromStationId, Guid ToStationId, double Cost, double DistanceMm);

public record GetRouteCostQuery(Guid FromStationId, Guid ToStationId) : IQuery<RouteCostDto>;

public class GetRouteCostQueryHandler : IQueryHandler<GetRouteCostQuery, RouteCostDto>
{
    private readonly IRouteEdgeRepository _edgeRepo;
    private readonly IDistributedCache _cache;

    public GetRouteCostQueryHandler(IRouteEdgeRepository edgeRepo, IDistributedCache cache)
    {
        _edgeRepo = edgeRepo;
        _cache = cache;
    }

    public async Task<Result<RouteCostDto>> Handle(GetRouteCostQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"route-cost:{request.FromStationId}:{request.ToStationId}";

        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached != null)
        {
            var parts = cached.Split('|');
            if (parts.Length == 2 && double.TryParse(parts[0], out var cachedCost) && double.TryParse(parts[1], out var cachedDist))
                return Result<RouteCostDto>.Success(new RouteCostDto(request.FromStationId, request.ToStationId, cachedCost, cachedDist));
        }

        var edge = await _edgeRepo.GetBetweenAsync(request.FromStationId, request.ToStationId, cancellationToken);
        if (edge == null)
            return Result<RouteCostDto>.Failure($"No route found between stations {request.FromStationId} and {request.ToStationId}.");

        var dto = new RouteCostDto(request.FromStationId, request.ToStationId, edge.Cost, edge.Distance);

        await _cache.SetStringAsync(cacheKey, $"{dto.Cost}|{dto.DistanceMm}",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15) },
            cancellationToken);

        return Result<RouteCostDto>.Success(dto);
    }
}
