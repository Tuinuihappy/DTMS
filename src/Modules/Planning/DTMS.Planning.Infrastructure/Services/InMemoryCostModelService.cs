using AMR.DeliveryPlanning.Planning.Domain.Services;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

public class InMemoryCostModelService : ICostModelService
{
    private readonly Dictionary<string, CostModelConfig> _configs = new();
    private CostModelConfig _default = new();

    public CostModelConfig GetConfig(string? vehicleTypeKey = null)
    {
        if (vehicleTypeKey != null && _configs.TryGetValue(vehicleTypeKey, out var cfg))
            return cfg;
        return _default;
    }

    public void UpdateConfig(CostModelConfig config, string? vehicleTypeKey = null)
    {
        if (vehicleTypeKey == null)
            _default = config;
        else
            _configs[vehicleTypeKey] = config;
    }

    public double ComputeScore(VehicleCandidate candidate, CostModelConfig config)
    {
        // Lower score = better candidate
        var distanceScore = candidate.DistanceToPickup * config.TravelDistanceWeight;

        // Battery penalty: critically low → heavy penalty, low → moderate, else small reward
        double batteryPenalty;
        if (candidate.BatteryLevel <= config.CriticalBatteryThresholdPct)
            batteryPenalty = 200.0 * config.BatteryBurnWeight;
        else if (candidate.BatteryLevel <= config.LowBatteryThresholdPct)
            batteryPenalty = 80.0 * config.BatteryBurnWeight;
        else
            batteryPenalty = (100.0 - candidate.BatteryLevel) * config.BatteryBurnWeight * 0.1;

        return distanceScore + batteryPenalty;
    }
}
