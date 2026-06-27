namespace AMR.DeliveryPlanning.Planning.Domain.Services;

public record CostModelConfig(
    double TravelDistanceWeight = 1.0,
    double BatteryBurnWeight = 0.5,
    double SlaPenaltyWeight = 2.0,
    double LowBatteryThresholdPct = 30.0,
    double CriticalBatteryThresholdPct = 15.0);

public interface ICostModelService
{
    CostModelConfig GetConfig(string? vehicleTypeKey = null);
    void UpdateConfig(CostModelConfig config, string? vehicleTypeKey = null);
    double ComputeScore(VehicleCandidate candidate, CostModelConfig config);
}
