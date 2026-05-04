using AMR.DeliveryPlanning.Fleet.Application.Services;
using AMR.DeliveryPlanning.Planning.Domain.Services;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

public class FleetVehicleProvider : IFleetVehicleProvider
{
    private readonly IFleetReadService _fleetReadService;
    private readonly IRouteCostCalculator _routeCostCalc;

    public FleetVehicleProvider(IFleetReadService fleetReadService, IRouteCostCalculator routeCostCalc)
    {
        _fleetReadService = fleetReadService;
        _routeCostCalc = routeCostCalc;
    }

    public async Task<List<VehicleCandidate>> GetIdleVehiclesAsync(Guid pickupStationId, CancellationToken cancellationToken = default)
    {
        var vehicles = await _fleetReadService.GetIdleVehiclesAsync(cancellationToken);

        // Fetch all route costs in parallel — one Redis lookup per vehicle, not sequential
        var distanceTasks = vehicles.Select(v => v.CurrentNodeId.HasValue
            ? _routeCostCalc.CalculateCostAsync(v.CurrentNodeId.Value, pickupStationId, cancellationToken)
            : Task.FromResult(999.0));
        var distances = await Task.WhenAll(distanceTasks);

        var candidates = new List<VehicleCandidate>(vehicles.Count);
        for (var i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            candidates.Add(new VehicleCandidate(
                VehicleId: v.VehicleId,
                DistanceToPickup: distances[i],
                BatteryLevel: v.BatteryLevel,
                VehicleTypeId: v.VehicleTypeId,
                Capabilities: v.Capabilities));
        }

        return candidates;
    }
}
