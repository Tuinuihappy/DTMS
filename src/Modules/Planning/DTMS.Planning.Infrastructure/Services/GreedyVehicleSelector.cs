using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

public class GreedyVehicleSelector : IVehicleSelector
{
    private readonly IFleetVehicleProvider _fleetProvider;
    private readonly ICostModelService _costModel;
    private readonly ILogger<GreedyVehicleSelector> _logger;

    public GreedyVehicleSelector(
        IFleetVehicleProvider fleetProvider,
        ICostModelService costModel,
        ILogger<GreedyVehicleSelector> logger)
    {
        _fleetProvider = fleetProvider;
        _costModel = costModel;
        _logger = logger;
    }

    public async Task<VehicleCandidate?> SelectBestVehicleAsync(
        Guid pickupStationId,
        string? requiredCapability = null,
        CancellationToken cancellationToken = default)
    {
        var dbCandidates = await _fleetProvider.GetIdleVehiclesAsync(pickupStationId, cancellationToken);

        var filtered = FilterByCapability(dbCandidates, requiredCapability).ToList();
        var best = SelectByScore(filtered);

        if (best != null)
        {
            _logger.LogInformation(
                "Selected vehicle {VehicleId} (battery={Battery}%, dist={Dist}, capability={Cap})",
                best.VehicleId, best.BatteryLevel, best.DistanceToPickup, requiredCapability ?? "any");
            return best;
        }

        _logger.LogWarning(
            "No available vehicle for pickup station {StationId} (capability={Cap}, idle vehicles={Total})",
            pickupStationId, requiredCapability ?? "any", dbCandidates.Count);

        return null;
    }

    private VehicleCandidate? SelectByScore(List<VehicleCandidate> candidates)
    {
        if (candidates.Count == 0) return null;
        var config = _costModel.GetConfig();
        return candidates.MinBy(c => _costModel.ComputeScore(c, config));
    }

    private static IEnumerable<VehicleCandidate> FilterByCapability(
        IEnumerable<VehicleCandidate> candidates, string? requiredCapability)
    {
        if (string.IsNullOrEmpty(requiredCapability)) return candidates;
        return candidates.Where(c =>
            c.Capabilities == null ||
            c.Capabilities.Count == 0 ||
            c.Capabilities.Any(cap => cap.Equals(requiredCapability, StringComparison.OrdinalIgnoreCase)));
    }
}
