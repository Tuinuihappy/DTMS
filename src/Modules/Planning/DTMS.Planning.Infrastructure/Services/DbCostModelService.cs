using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data.Records;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

public class DbCostModelService : ICostModelService
{
    private readonly PlanningDbContext _db;
    // Request-scoped cache — populated lazily on first GetConfig call
    private Dictionary<string, CostModelConfig>? _cache;

    public DbCostModelService(PlanningDbContext db) => _db = db;

    public CostModelConfig GetConfig(string? vehicleTypeKey = null)
    {
        var cache = EnsureCache();
        if (vehicleTypeKey != null && cache.TryGetValue(vehicleTypeKey, out var specific))
            return specific;
        return cache.TryGetValue(string.Empty, out var def) ? def : new CostModelConfig();
    }

    public void UpdateConfig(CostModelConfig config, string? vehicleTypeKey = null)
    {
        var cache = EnsureCache();
        var key = vehicleTypeKey ?? string.Empty;

        // Upsert DB row
        var existing = _db.CostModelConfigs
            .FirstOrDefault(c => c.VehicleTypeKey == vehicleTypeKey);

        if (existing == null)
        {
            _db.CostModelConfigs.Add(ToRecord(config, vehicleTypeKey));
        }
        else
        {
            existing.TravelDistanceWeight = config.TravelDistanceWeight;
            existing.BatteryBurnWeight = config.BatteryBurnWeight;
            existing.SlaPenaltyWeight = config.SlaPenaltyWeight;
            existing.LowBatteryThresholdPct = config.LowBatteryThresholdPct;
            existing.CriticalBatteryThresholdPct = config.CriticalBatteryThresholdPct;
        }

        _db.SaveChanges();

        // Keep request-scoped cache in sync
        cache[key] = config;
    }

    public double ComputeScore(VehicleCandidate candidate, CostModelConfig config)
    {
        var distanceScore = candidate.DistanceToPickup * config.TravelDistanceWeight;

        double batteryPenalty;
        if (candidate.BatteryLevel <= config.CriticalBatteryThresholdPct)
            batteryPenalty = 200.0 * config.BatteryBurnWeight;
        else if (candidate.BatteryLevel <= config.LowBatteryThresholdPct)
            batteryPenalty = 80.0 * config.BatteryBurnWeight;
        else
            batteryPenalty = (100.0 - candidate.BatteryLevel) * config.BatteryBurnWeight * 0.1;

        return distanceScore + batteryPenalty;
    }

    private Dictionary<string, CostModelConfig> EnsureCache()
    {
        if (_cache != null) return _cache;

        var records = _db.CostModelConfigs.AsNoTracking().ToList();
        _cache = records.ToDictionary(
            r => r.VehicleTypeKey ?? string.Empty,
            r => new CostModelConfig(
                r.TravelDistanceWeight,
                r.BatteryBurnWeight,
                r.SlaPenaltyWeight,
                r.LowBatteryThresholdPct,
                r.CriticalBatteryThresholdPct));

        return _cache;
    }

    private static CostModelConfigRecord ToRecord(CostModelConfig c, string? vehicleTypeKey) => new()
    {
        Id = Guid.NewGuid(),
        VehicleTypeKey = vehicleTypeKey,
        TravelDistanceWeight = c.TravelDistanceWeight,
        BatteryBurnWeight = c.BatteryBurnWeight,
        SlaPenaltyWeight = c.SlaPenaltyWeight,
        LowBatteryThresholdPct = c.LowBatteryThresholdPct,
        CriticalBatteryThresholdPct = c.CriticalBatteryThresholdPct
    };
}
