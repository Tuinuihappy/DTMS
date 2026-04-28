using AMR.DeliveryPlanning.Dispatch.Domain.Entities;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Repositories;

public interface ITripRepository
{
    Task<Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Trip?> GetTripByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task<List<Trip>> GetActiveTripsByVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default);
    Task AddAsync(Trip trip, CancellationToken cancellationToken = default);
    Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default);
}
