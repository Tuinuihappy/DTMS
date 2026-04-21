using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

/// <summary>
/// Greedy vehicle selector: picks the closest idle vehicle with sufficient battery.
/// In MVP, this uses a local cache of vehicle states populated via Integration Events.
/// </summary>
public class GreedyVehicleSelector : IVehicleSelector
{
    private readonly ILogger<GreedyVehicleSelector> _logger;

    // In-memory cache of available vehicles (populated by event handlers in production)
    // For MVP, this acts as a simple placeholder until MassTransit consumers are wired up.
    private static readonly List<VehicleCandidate> _cachedVehicles = new();

    public GreedyVehicleSelector(ILogger<GreedyVehicleSelector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a vehicle into the local cache (called by Integration Event handler).
    /// </summary>
    public static void UpdateVehicleCache(Guid vehicleId, double distanceToOrigin, double batteryLevel)
    {
        _cachedVehicles.RemoveAll(v => v.VehicleId == vehicleId);
        if (batteryLevel > 20) // Only consider vehicles with > 20% battery
        {
            _cachedVehicles.Add(new VehicleCandidate(vehicleId, distanceToOrigin, batteryLevel));
        }
    }

    public Task<VehicleCandidate?> SelectBestVehicleAsync(Guid pickupStationId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Selecting best vehicle for pickup station {StationId} from {Count} candidates",
            pickupStationId, _cachedVehicles.Count);

        // Greedy: pick the vehicle with lowest distance, breaking ties by highest battery
        var best = _cachedVehicles
            .OrderBy(v => v.DistanceToPickup)
            .ThenByDescending(v => v.BatteryLevel)
            .FirstOrDefault();

        if (best != null)
        {
            _logger.LogInformation("Selected vehicle {VehicleId} (distance={Distance}, battery={Battery}%)",
                best.VehicleId, best.DistanceToPickup, best.BatteryLevel);
        }
        else
        {
            _logger.LogWarning("No available vehicles found for pickup station {StationId}", pickupStationId);
        }

        return Task.FromResult(best);
    }
}
