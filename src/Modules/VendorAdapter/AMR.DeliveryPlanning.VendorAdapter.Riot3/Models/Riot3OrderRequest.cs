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

    // Nullable because MOVE missions don't carry this field per the RIOT3
    // spec example — only ACT missions emit "blockingType". The adapter
    // sets "NONE" on ACT and leaves null on MOVE; the serializer
    // (WhenWritingNull) drops it from MOVE on the wire.
    [JsonPropertyName("blockingType")]
    public string? BlockingType { get; set; }

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

    // RIOT3 spec sends mixed types: id/param0/param1 as JSON numbers, param_str
    // as a string. Typed as `object?` so the serializer emits the actual JSON
    // shape (`"value": 131`, not `"value": "131"`) — callers pass the raw value
    // (int, string, etc.) and System.Text.Json picks the right token.
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}
