using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Services;

// NOTE: Response shapes are modelled from RIOT3 v4 API conventions.
//       Verify against actual vendor spec before go-live.

internal sealed class Riot3RouteCostResponse
{
    [JsonPropertyName("mapId")]
    public string MapId { get; set; } = string.Empty;

    [JsonPropertyName("stationId")]
    public string StationId { get; set; } = string.Empty;

    [JsonPropertyName("costs")]
    public List<Riot3RouteCostEntry> Costs { get; set; } = new();
}

internal sealed class Riot3RouteCostEntry
{
    [JsonPropertyName("targetStationId")]
    public string TargetStationId { get; set; } = string.Empty;

    [JsonPropertyName("cost")]
    public double Cost { get; set; }

    [JsonPropertyName("distance")]
    public double Distance { get; set; }
}
