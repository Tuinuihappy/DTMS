using AMR.DeliveryPlanning.Fleet.Domain.Enums;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

public class FleetVehicleProvider : IFleetVehicleProvider
{
    private readonly FleetDbContext _fleetDb;

    public FleetVehicleProvider(FleetDbContext fleetDb) => _fleetDb = fleetDb;

    public async Task<List<VehicleCandidate>> GetIdleVehiclesAsync(CancellationToken cancellationToken = default)
    {
        var vehicles = await _fleetDb.Vehicles
            .Where(v => v.State == VehicleState.Idle && v.BatteryLevel > 20)
            .ToListAsync(cancellationToken);

        // Load vehicle types with capabilities
        var typeIds = vehicles.Select(v => v.VehicleTypeId).Distinct().ToList();
        var vehicleTypes = await _fleetDb.VehicleTypes
            .Where(vt => typeIds.Contains(vt.Id))
            .ToDictionaryAsync(vt => vt.Id, cancellationToken);

        return vehicles.Select(v =>
        {
            vehicleTypes.TryGetValue(v.VehicleTypeId, out var vt);
            return new VehicleCandidate(
                VehicleId: v.Id,
                DistanceToPickup: 10.0,
                BatteryLevel: v.BatteryLevel,
                VehicleTypeId: v.VehicleTypeId,
                Capabilities: vt?.Capabilities);
        }).ToList();
    }
}
