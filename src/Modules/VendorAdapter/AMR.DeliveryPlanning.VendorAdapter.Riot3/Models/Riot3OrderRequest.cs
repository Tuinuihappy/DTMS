using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

public class Riot3OrderRequest
{
    [JsonPropertyName("upperKey")]
    public string UpperKey { get; set; } = string.Empty;

    [JsonPropertyName("orderName")]
    public string OrderName { get; set; } = string.Empty;

    [JsonPropertyName("orderType")]
    public string OrderType { get; set; } = "WORK";

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 10;

    [JsonPropertyName("structureType")]
    public string StructureType { get; set; } = "sequence";

    [JsonPropertyName("appointVehicleKey")]
    public string? AppointVehicleKey { get; set; }

    [JsonPropertyName("missions")]
    public List<Riot3Mission> Missions { get; set; } = new();
}

public class Riot3Mission
{
    [JsonPropertyName("missionId")]
    public string MissionId { get; set; } = string.Empty;

    [JsonPropertyName("missionName")]
    public string MissionName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("mapId")]
    public string? MapId { get; set; }

    [JsonPropertyName("stationId")]
    public string? StationId { get; set; }

    [JsonPropertyName("blockingType")]
    public string BlockingType { get; set; } = "HARD";

    [JsonPropertyName("actionType")]
    public int? ActionType { get; set; }

    [JsonPropertyName("parameters")]
    public List<Riot3ActionParam>? Parameters { get; set; }

    [JsonPropertyName("url")]
    public string? CallbackUrl { get; set; }

    [JsonPropertyName("retryCount")]
    public int? RetryCount { get; set; }

    [JsonPropertyName("backoffDelay")]
    public int? BackoffDelay { get; set; }

    [JsonPropertyName("readTimeOutMillis")]
    public int? ReadTimeOutMillis { get; set; }
}

public class Riot3ActionParam
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}
