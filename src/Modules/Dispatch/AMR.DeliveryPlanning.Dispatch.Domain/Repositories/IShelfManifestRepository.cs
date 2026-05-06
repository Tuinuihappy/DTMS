using AMR.DeliveryPlanning.Dispatch.Domain.Entities;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Repositories;

public interface IShelfManifestRepository
{
    Task<ShelfManifest?> GetByJobIdAsync(Guid jobId, CancellationToken ct = default);
    Task<ShelfManifest?> GetByTripIdAsync(Guid tripId, CancellationToken ct = default);
    Task<List<ShelfManifest>> GetByShelfRfidAsync(string shelfRfid, CancellationToken ct = default);
    Task AddAsync(ShelfManifest manifest, CancellationToken ct = default);
    Task UpdateAsync(ShelfManifest manifest, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
