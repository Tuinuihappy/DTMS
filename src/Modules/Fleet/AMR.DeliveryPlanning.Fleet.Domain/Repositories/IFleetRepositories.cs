using AMR.DeliveryPlanning.Fleet.Domain.Entities;

namespace AMR.DeliveryPlanning.Fleet.Domain.Repositories;

public interface IVehicleRepository
{
    Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Vehicle>> GetAvailableVehiclesAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default);
    void Update(Vehicle vehicle);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IVehicleTypeRepository
{
    Task<VehicleType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(VehicleType vehicleType, CancellationToken cancellationToken = default);
}
