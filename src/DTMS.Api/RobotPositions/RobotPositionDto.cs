namespace DTMS.Api.RobotPositions;

// Public DTO carried by both the in-memory store and the GET endpoint.
// `MapId` is the DTMS Guid (resolved from RIOT3's int mapId via Map.VendorRef),
// so the frontend filter is a straight equality check on the column it already
// uses everywhere else.
public sealed record RobotPositionDto(
    string DeviceKey,
    string DeviceName,
    Guid MapId,
    int VendorMapId,
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
    string? StationName,
    DateTime UpdatedAtUtc);
