using AMR.DeliveryPlanning.Fleet.Domain.Enums;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

public class FleetVehicleProvider : IFleetVehicleProvider
{
    private readonly FleetDbContext _fleetDb;
    private readonly IRouteCostCalculator _routeCostCalc;

    public FleetVehicleProvider(FleetDbContext fleetDb, IRouteCostCalculator routeCostCalc)
    {
        _fleetDb = fleetDb;
        _routeCostCalc = routeCostCalc;
    }

    public async Task<List<VehicleCandidate>> GetIdleVehiclesAsync(Guid pickupStationId, CancellationToken cancellationToken = default)
    {
        var vehicles = await _fleetDb.Vehicles
            .Where(v => v.State == VehicleState.Idle && v.BatteryLevel > 20)
            .ToListAsync(cancellationToken);

        var typeIds = vehicles.Select(v => v.VehicleTypeId).Distinct().ToList();
        var vehicleTypes = await _fleetDb.VehicleTypes
            .Where(vt => typeIds.Contains(vt.Id))
            .ToDictionaryAsync(vt => vt.Id, cancellationToken);

        // Fetch all route costs in parallel — one Redis lookup per vehicle, not sequential
        var distanceTasks = vehicles.Select(v => v.CurrentNodeId.HasValue
            ? _routeCostCalc.CalculateCostAsync(v.CurrentNodeId.Value, pickupStationId, cancellationToken)
            : Task.FromResult(999.0));
        var distances = await Task.WhenAll(distanceTasks);

        var candidates = new List<VehicleCandidate>(vehicles.Count);
        for (var i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            vehicleTypes.TryGetValue(v.VehicleTypeId, out var vt);
            candidates.Add(new VehicleCandidate(
                VehicleId: v.Id,
                DistanceToPickup: distances[i],
                BatteryLevel: v.BatteryLevel,
                VehicleTypeId: v.VehicleTypeId,
                Capabilities: vt?.Capabilities));
        }

        return candidates;
    }
}
