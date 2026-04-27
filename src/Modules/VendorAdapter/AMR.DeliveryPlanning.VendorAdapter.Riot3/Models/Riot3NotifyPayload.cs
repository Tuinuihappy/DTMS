using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

public class Riot3NotifyPayload
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("taskEventType")]
    public string? TaskEventType { get; set; }

    [JsonPropertyName("vehicleEventType")]
    public string? VehicleEventType { get; set; }

    [JsonPropertyName("orderKey")]
    public string? OrderKey { get; set; }

    [JsonPropertyName("upperKey")]
    public string? UpperKey { get; set; }

    [JsonPropertyName("failResult")]
    public Riot3FailResult? FailResult { get; set; }

    [JsonPropertyName("vehicle")]
    public Riot3VehicleInfo? Vehicle { get; set; }

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("progress")]
    public int? Progress { get; set; }
}

public class Riot3FailResult
{
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorMsg")]
    public string? ErrorMsg { get; set; }
}

public class Riot3VehicleInfo
{
    [JsonPropertyName("deviceKey")]
    public string DeviceKey { get; set; } = string.Empty;

    [JsonPropertyName("batteryLevel")]
    public int BatteryLevel { get; set; }

    [JsonPropertyName("systemState")]
    public string? SystemState { get; set; }

    [JsonPropertyName("safetyState")]
    public string? SafetyState { get; set; }
}
