namespace AMR.DeliveryPlanning.Planning.Infrastructure.Data.Records;

// Persistence record — not a domain entity.
// null VehicleTypeKey = the global default config row.
public class CostModelConfigRecord
{
    public Guid Id { get; set; }
    public string? VehicleTypeKey { get; set; }
    public double TravelDistanceWeight { get; set; } = 1.0;
    public double BatteryBurnWeight { get; set; } = 0.5;
    public double SlaPenaltyWeight { get; set; } = 2.0;
    public double LowBatteryThresholdPct { get; set; } = 30.0;
    public double CriticalBatteryThresholdPct { get; set; } = 15.0;
}
