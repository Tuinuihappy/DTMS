using DTMS.Facility.Domain.Entities;

namespace DTMS.Facility.Domain.Repositories;

public interface IMapRepository
{
    Task<Map?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Map?> GetByVendorRefAsync(string vendorRef, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Map>> ListAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Map map, CancellationToken cancellationToken = default);
    void Update(Map map);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IStationRepository
{
    Task<Station?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Station>> GetByMapAsync(Guid mapId, CancellationToken cancellationToken = default);
    Task<List<Station>> GetAllByMapAsync(Guid mapId, CancellationToken cancellationToken = default);
    Task<List<Station>> QueryAsync(Guid? mapId, StationType? type, string? compatibleVehicleType, bool includeInactive = false, string? code = null, CancellationToken cancellationToken = default);
    Task AddAsync(Station station, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
