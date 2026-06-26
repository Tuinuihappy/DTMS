namespace AMR.DeliveryPlanning.Fleet.Domain.Enums;

public enum VehicleState
{
    Offline,
    Idle,
    Moving,
    Working, // e.g. Lifting
    Charging,
    Error,
    Maintenance
}
