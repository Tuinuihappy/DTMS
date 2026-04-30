using AMR.DeliveryPlanning.Fleet.Domain.Entities;

namespace AMR.DeliveryPlanning.Fleet.Domain.Repositories;

public interface IVehicleRepository
{
    Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Vehicle>> GetAvailableVehiclesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Vehicle>> GetByGroupAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default);
    void Update(Vehicle vehicle);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IVehicleTypeRepository
{
    Task<VehicleType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(VehicleType vehicleType, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IChargingPolicyRepository
{
    Task<ChargingPolicy?> GetByVehicleTypeAsync(Guid vehicleTypeId, CancellationToken cancellationToken = default);
    Task<List<ChargingPolicy>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(ChargingPolicy policy, CancellationToken cancellationToken = default);
    Task UpdateAsync(ChargingPolicy policy, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IMaintenanceRecordRepository
{
    Task<MaintenanceRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<MaintenanceRecord>> GetByVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default);
    Task AddAsync(MaintenanceRecord record, CancellationToken cancellationToken = default);
    Task UpdateAsync(MaintenanceRecord record, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IVehicleGroupRepository
{
    Task<VehicleGroup?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<VehicleGroup>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(VehicleGroup group, CancellationToken cancellationToken = default);
    Task UpdateAsync(VehicleGroup group, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
