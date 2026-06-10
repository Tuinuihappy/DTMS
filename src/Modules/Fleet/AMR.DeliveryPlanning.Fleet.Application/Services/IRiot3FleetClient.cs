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

// Wider projection used by the live-position poller. Pulls everything the
// map page wants to render in one shot (pose, battery, state, current order,
// emergency / charging flags) so the SSE/poll endpoint has a single source.
public record Riot3RobotLiveInfo(
    string DeviceKey,
    string DeviceName,
    int? MapId,
    double X,
    double Y,
    double Theta,
    string SystemState,
    string ConnectionState,
    bool Emergency,
    bool Paused,
    int BatteryPercentage,
    bool Charging,
    string? OrderKey,
    string? OrderName,
    string? StartToEnd,
    string? StationName);

public interface IRiot3FleetClient
{
    Task<List<Riot3RobotInfo>> GetAllRobotsAsync(CancellationToken ct = default);
    Task<List<Riot3RobotLiveInfo>> GetRobotLivePositionsAsync(CancellationToken ct = default);
}
