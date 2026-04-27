using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

public class Riot3VehicleResponse
{
    [JsonPropertyName("deviceKey")]
    public string DeviceKey { get; set; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("batteryLevel")]
    public int BatteryLevel { get; set; }

    [JsonPropertyName("systemState")]
    public string SystemState { get; set; } = string.Empty;

    [JsonPropertyName("safetyState")]
    public string? SafetyState { get; set; }

    [JsonPropertyName("connectionState")]
    public string? ConnectionState { get; set; }

    [JsonPropertyName("position")]
    public Riot3Position? Position { get; set; }

    [JsonPropertyName("currentOrderKey")]
    public string? CurrentOrderKey { get; set; }
}

public class Riot3Position
{
    [JsonPropertyName("mapId")]
    public string? MapId { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("theta")]
    public double Theta { get; set; }
}

public class Riot3VehicleListResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("robots")]
    public List<Riot3VehicleResponse> Robots { get; set; } = new();
}
