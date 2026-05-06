using AMR.DeliveryPlanning.Facility.Domain.Entities;

namespace AMR.DeliveryPlanning.Facility.Domain.Repositories;

public interface ICarrierTypeProfileRepository
{
    Task<CarrierTypeProfile?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<CarrierTypeProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<CarrierTypeProfile>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(CarrierTypeProfile profile, CancellationToken ct = default);
    Task UpdateAsync(CarrierTypeProfile profile, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
