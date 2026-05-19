namespace AMR.DeliveryPlanning.Fleet.Application.Services;

public record Riot3RobotInfo(
    string DeviceKey,
    string DeviceName,
    string TypeKey,
    string DriverKey,
    string ConnectionState,
    string SystemState,
    int BatteryPercentage,
    int? StationId);

public interface IRiot3FleetClient
{
    Task<List<Riot3RobotInfo>> GetAllRobotsAsync(CancellationToken ct = default);
}
