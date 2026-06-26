using AMR.DeliveryPlanning.Facility.Domain.Entities;

namespace AMR.DeliveryPlanning.Facility.Domain.Repositories;

public interface ILoadUnitProfileRepository
{
    Task<LoadUnitProfile?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<LoadUnitProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<LoadUnitProfile>> GetAllAsync(CancellationToken ct = default);
    Task<List<LoadUnitProfile>> GetByCarrierTypeAsync(string carrierTypeCode, CancellationToken ct = default);
    Task AddAsync(LoadUnitProfile profile, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
