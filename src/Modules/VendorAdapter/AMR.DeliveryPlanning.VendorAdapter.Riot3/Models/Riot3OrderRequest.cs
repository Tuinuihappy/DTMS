using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

// Matches RIOT3.0 API v4 "Create Order" — POST /api/v4/orders
public class Riot3OrderRequest
{
    [JsonPropertyName("upperKey")]
    public string UpperKey { get; set; } = string.Empty;

    [JsonPropertyName("orderName")]
    public string? OrderName { get; set; }

    [JsonPropertyName("orderType")]
    public string OrderType { get; set; } = "WORK";

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 10;

    [JsonPropertyName("structureType")]
    public string StructureType { get; set; } = "sequence";

    [JsonPropertyName("missions")]
    public List<Riot3Mission> Missions { get; set; } = new();

    [JsonPropertyName("appointVehicleKey")]
    public string? AppointVehicleKey { get; set; }

    [JsonPropertyName("appointVehicleName")]
    public string? AppointVehicleName { get; set; }

    // Comma-separated group keys when multiple groups are acceptable
    [JsonPropertyName("appointVehicleGroupKey")]
    public string? AppointVehicleGroupKey { get; set; }

    [JsonPropertyName("appointVehicleGroupName")]
    public string? AppointVehicleGroupName { get; set; }

    [JsonPropertyName("orderState")]
    public string? OrderState { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}

public class Riot3Mission
{
    [JsonPropertyName("missionIndex")]
    public int? MissionIndex { get; set; }

    // "MOVE" or "ACT"
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    // "agv" | "seer_agv" | "rest_20" — required per spec
    [JsonPropertyName("category")]
    public string Category { get; set; } = "agv";

    // String per spec (e.g. "standardRobotsCustom"), not an integer
    [JsonPropertyName("actionType")]
    public string? ActionType { get; set; }

    [JsonPropertyName("blockingType")]
    public string BlockingType { get; set; } = "NONE";

    [JsonPropertyName("missionKey")]
    public string? MissionKey { get; set; }

    [JsonPropertyName("actionName")]
    public string? ActionName { get; set; }

    [JsonPropertyName("actionDescription")]
    public string? ActionDescription { get; set; }

    [JsonPropertyName("actionParameters")]
    public List<Riot3ActionParam>? ActionParameters { get; set; }

    [JsonPropertyName("extendedParameters")]
    public List<Riot3ActionParam>? ExtendedParameters { get; set; }

    [JsonPropertyName("mapId")]
    public int? MapId { get; set; }

    [JsonPropertyName("mapName")]
    public string? MapName { get; set; }

    [JsonPropertyName("stationId")]
    public int? StationId { get; set; }

    [JsonPropertyName("stationName")]
    public string? StationName { get; set; }
}

public class Riot3ActionParam
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    // Spec example shows mixed types (int, string); keep as string for
    // serialization simplicity — callers stringify before passing in.
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}
