using AMR.DeliveryPlanning.Fleet.Domain.Enums;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

/// <summary>
/// Queries the Fleet module's database for idle vehicles with sufficient battery.
/// This is the cross-module bridge: Planning.Infrastructure → Fleet.Infrastructure.
/// </summary>
public class FleetVehicleProvider : IFleetVehicleProvider
{
    private readonly FleetDbContext _fleetDb;

    public FleetVehicleProvider(FleetDbContext fleetDb)
    {
        _fleetDb = fleetDb;
    }

    public async Task<List<VehicleCandidate>> GetIdleVehiclesAsync(CancellationToken cancellationToken = default)
    {
        var vehicles = await _fleetDb.Vehicles
            .Where(v => v.State == VehicleState.Idle && v.BatteryLevel > 20)
            .ToListAsync(cancellationToken);

        return vehicles.Select(v => new VehicleCandidate(
            v.Id,
            DistanceToPickup: 10.0, // Default distance; will use real map cost in future
            v.BatteryLevel
        )).ToList();
    }
}
