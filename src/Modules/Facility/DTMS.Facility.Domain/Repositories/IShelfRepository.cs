using AMR.DeliveryPlanning.Facility.Domain.Entities;

namespace AMR.DeliveryPlanning.Facility.Domain.Repositories;

public interface IShelfRepository
{
    Task<Shelf?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Shelf?> GetByRfidAsync(string rfid, CancellationToken ct = default);
    Task<List<Shelf>> GetByMapAsync(Guid mapId, CancellationToken ct = default);
    Task<List<Shelf>> GetAllAvailableAsync(double requiredWeightKg, int requiredSlots, CancellationToken ct = default);
    Task AddAsync(Shelf shelf, CancellationToken ct = default);
    Task UpdateAsync(Shelf shelf, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
