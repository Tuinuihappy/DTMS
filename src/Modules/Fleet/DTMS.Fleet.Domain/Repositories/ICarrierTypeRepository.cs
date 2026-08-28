using DTMS.Fleet.Domain.Entities;

namespace DTMS.Fleet.Domain.Repositories;

public interface ICarrierTypeRepository
{
    Task<CarrierType?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<CarrierType?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<CarrierType>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(CarrierType carrierType, CancellationToken ct = default);
    Task UpdateAsync(CarrierType carrierType, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
