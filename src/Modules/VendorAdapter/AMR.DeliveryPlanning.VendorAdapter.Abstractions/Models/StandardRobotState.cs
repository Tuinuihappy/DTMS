namespace AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;

public enum StandardState
{
    Offline,
    Idle,
    Moving,
    Working,
    Charging,
    Error,
    Maintenance
}

public class StandardRobotState
{
    public Guid VehicleId { get; set; }
    public StandardState State { get; set; }
    public double BatteryLevel { get; set; }
    public Guid? CurrentNodeId { get; set; }
    public double? CurrentX { get; set; }
    public double? CurrentY { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; }
}
