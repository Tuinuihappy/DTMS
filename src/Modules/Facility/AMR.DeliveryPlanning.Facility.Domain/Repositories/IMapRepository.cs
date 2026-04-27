using AMR.DeliveryPlanning.Facility.Domain.Entities;

namespace AMR.DeliveryPlanning.Facility.Domain.Repositories;

public interface IMapRepository
{
    Task<Map?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Map map, CancellationToken cancellationToken = default);
    void Update(Map map);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IStationRepository
{
    Task<Station?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Station>> GetByMapAsync(Guid mapId, CancellationToken cancellationToken = default);
    Task<List<Station>> QueryAsync(Guid? mapId, StationType? type, Guid? zoneId, string? compatibleVehicleType, CancellationToken cancellationToken = default);
    Task AddAsync(Station station, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IRouteEdgeRepository
{
    Task<RouteEdge?> GetBetweenAsync(Guid fromStationId, Guid toStationId, CancellationToken cancellationToken = default);
    Task<List<RouteEdge>> GetByMapAsync(Guid mapId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITopologyOverlayRepository
{
    Task<TopologyOverlay?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<TopologyOverlay>> GetActiveByMapAsync(Guid mapId, CancellationToken cancellationToken = default);
    Task<List<TopologyOverlay>> GetExpiredAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TopologyOverlay overlay, CancellationToken cancellationToken = default);
    Task UpdateAsync(TopologyOverlay overlay, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IFacilityResourceRepository
{
    Task<FacilityResource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<FacilityResource>> GetByMapAsync(Guid mapId, CancellationToken cancellationToken = default);
    Task AddAsync(FacilityResource resource, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
