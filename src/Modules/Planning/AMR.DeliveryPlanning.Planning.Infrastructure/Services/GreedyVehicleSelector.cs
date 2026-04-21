using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

/// <summary>
/// Greedy vehicle selector that queries the Fleet module's DB for idle vehicles.
/// Falls back to the in-memory cache for backward compatibility in tests.
/// </summary>
public class GreedyVehicleSelector : IVehicleSelector
{
    private readonly IFleetVehicleProvider _fleetProvider;
    private readonly ILogger<GreedyVehicleSelector> _logger;

    // Static cache kept for integration test seeding (backward compat)
    private static readonly List<VehicleCandidate> _cachedVehicles = new();

    public GreedyVehicleSelector(IFleetVehicleProvider fleetProvider, ILogger<GreedyVehicleSelector> logger)
    {
        _fleetProvider = fleetProvider;
        _logger = logger;
    }

    /// <summary>
    /// Register a vehicle into the local cache (used by tests and event handlers).
    /// </summary>
    public static void UpdateVehicleCache(Guid vehicleId, double distanceToOrigin, double batteryLevel)
    {
        _cachedVehicles.RemoveAll(v => v.VehicleId == vehicleId);
        if (batteryLevel > 20)
        {
            _cachedVehicles.Add(new VehicleCandidate(vehicleId, distanceToOrigin, batteryLevel));
        }
    }

    public async Task<VehicleCandidate?> SelectBestVehicleAsync(Guid pickupStationId, CancellationToken cancellationToken = default)
    {
        // 1. Try Fleet DB first
        var dbCandidates = await _fleetProvider.GetIdleVehiclesAsync(cancellationToken);

        if (dbCandidates.Count > 0)
        {
            _logger.LogInformation("Found {Count} idle vehicles from Fleet DB", dbCandidates.Count);

            var best = dbCandidates
                .OrderBy(v => v.DistanceToPickup)
                .ThenByDescending(v => v.BatteryLevel)
                .First();

            _logger.LogInformation("Selected vehicle {VehicleId} from Fleet DB (battery={Battery}%)",
                best.VehicleId, best.BatteryLevel);
            return best;
        }

        // 2. Fallback to in-memory cache (tests, event-seeded)
        _logger.LogInformation("Fleet DB empty, checking in-memory cache ({Count} candidates)", _cachedVehicles.Count);

        var cached = _cachedVehicles
            .OrderBy(v => v.DistanceToPickup)
            .ThenByDescending(v => v.BatteryLevel)
            .FirstOrDefault();

        if (cached != null)
        {
            _logger.LogInformation("Selected vehicle {VehicleId} from cache", cached.VehicleId);
        }
        else
        {
            _logger.LogWarning("No available vehicles found for pickup station {StationId}", pickupStationId);
        }

        return cached;
    }
}
