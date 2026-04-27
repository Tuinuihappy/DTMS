using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Fleet.Domain.Entities;

public enum ChargingMode { Opportunistic, Scheduled, Reserved }

public class ChargingPolicy : Entity<Guid>
{
    public Guid VehicleTypeId { get; private set; }
    public double LowThresholdPct { get; private set; }
    public double TargetThresholdPct { get; private set; }
    public ChargingMode Mode { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ChargingPolicy() { }

    public ChargingPolicy(Guid vehicleTypeId, double lowThresholdPct, double targetThresholdPct, ChargingMode mode)
    {
        Id = Guid.NewGuid();
        VehicleTypeId = vehicleTypeId;
        LowThresholdPct = lowThresholdPct;
        TargetThresholdPct = targetThresholdPct;
        Mode = mode;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(double lowThresholdPct, double targetThresholdPct, ChargingMode mode)
    {
        LowThresholdPct = lowThresholdPct;
        TargetThresholdPct = targetThresholdPct;
        Mode = mode;
    }

    public bool ShouldCharge(double currentBatteryPct) => currentBatteryPct <= LowThresholdPct;
}
