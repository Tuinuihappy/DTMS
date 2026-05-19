using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AMR.DeliveryPlanning.Fleet.Application.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Services;

public sealed class Riot3FleetClient : IRiot3FleetClient
{
    private readonly HttpClient _http;
    private readonly ILogger<Riot3FleetClient> _logger;

    public Riot3FleetClient(HttpClient http, ILogger<Riot3FleetClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<Riot3RobotInfo>> GetAllRobotsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<Riot3RobotListResponse>(
                "/api/v4/robots?pageSize=-1", ct);

            return response?.Data?.Records
                .Select(r => new Riot3RobotInfo(
                    DeviceKey: r.DeviceKey,
                    DeviceName: r.DeviceName,
                    TypeKey: r.DeviceInfo?.TypeKey ?? string.Empty,
                    DriverKey: r.DeviceInfo?.DriverKey ?? "standard-agv-driver",
                    ConnectionState: r.ConnectionInfo?.ConnectionState ?? "UNKNOWN",
                    SystemState: r.BasicStatus?.SystemState ?? "NONE",
                    BatteryPercentage: r.BasicStatus?.BatteryInfo?.BatteryPercentage ?? 0,
                    StationId: r.PositionInfo?.StationId))
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch robot list from RIOT3");
            return [];
        }
    }
}

// ── JSON response models ──────────────────────────────────────────────────────

file sealed class Riot3RobotListResponse
{
    [JsonPropertyName("data")] public Riot3RobotPageData? Data { get; set; }
}

file sealed class Riot3RobotPageData
{
    [JsonPropertyName("records")] public List<Riot3RobotRecord> Records { get; set; } = [];
}

file sealed class Riot3RobotRecord
{
    [JsonPropertyName("deviceKey")] public string DeviceKey { get; set; } = string.Empty;
    [JsonPropertyName("deviceName")] public string DeviceName { get; set; } = string.Empty;
    [JsonPropertyName("deviceInfo")] public Riot3DeviceInfo? DeviceInfo { get; set; }
    [JsonPropertyName("basicStatus")] public Riot3BasicStatus? BasicStatus { get; set; }
    [JsonPropertyName("connectionInfo")] public Riot3ConnectionInfo? ConnectionInfo { get; set; }
    [JsonPropertyName("positionInfo")] public Riot3PositionInfo? PositionInfo { get; set; }
}

file sealed class Riot3DeviceInfo
{
    [JsonPropertyName("typeKey")] public string TypeKey { get; set; } = string.Empty;
    [JsonPropertyName("driverKey")] public string DriverKey { get; set; } = string.Empty;
}

file sealed class Riot3BasicStatus
{
    [JsonPropertyName("systemState")] public string SystemState { get; set; } = "NONE";
    [JsonPropertyName("batteryInfo")] public Riot3BatteryInfo? BatteryInfo { get; set; }
}

file sealed class Riot3BatteryInfo
{
    [JsonPropertyName("batteryPercentage")] public int BatteryPercentage { get; set; }
}

file sealed class Riot3ConnectionInfo
{
    [JsonPropertyName("connectionState")] public string ConnectionState { get; set; } = "UNKNOWN";
}

file sealed class Riot3PositionInfo
{
    [JsonPropertyName("stationId")] public int? StationId { get; set; }
}
