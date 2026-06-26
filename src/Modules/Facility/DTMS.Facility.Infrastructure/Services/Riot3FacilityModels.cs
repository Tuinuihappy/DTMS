using System.Text.Json.Serialization;

namespace DTMS.Facility.Infrastructure.Services;

public sealed class Riot3MapListResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public Riot3MapPageData? Data { get; set; }
}

public sealed class Riot3MapPageData
{
    [JsonPropertyName("records")]
    public List<Riot3MapRecord> Records { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public sealed class Riot3MapRecord
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mapName")]
    public string MapName { get; set; } = string.Empty;

    [JsonPropertyName("mapInfoState")]
    public string MapInfoState { get; set; } = string.Empty;

    [JsonPropertyName("floor")]
    public int Floor { get; set; }
}

public sealed class Riot3StationListResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<Riot3StationRecord> Data { get; set; } = new();
}

public sealed class Riot3StationRecord
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("pos.x")]
    public double PosX { get; set; }

    [JsonPropertyName("pos.y")]
    public double PosY { get; set; }

    [JsonPropertyName("pos.yaw")]
    public double PosYaw { get; set; }
}
