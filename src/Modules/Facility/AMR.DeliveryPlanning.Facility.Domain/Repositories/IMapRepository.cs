using AMR.DeliveryPlanning.Facility.Domain.Entities;

namespace AMR.DeliveryPlanning.Facility.Domain.Repositories;

public interface IMapRepository
{
    Task<Map?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Map map, CancellationToken cancellationToken = default);
    void Update(Map map);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
