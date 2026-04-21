using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

public class RiotTaskRequest
{
    [JsonPropertyName("task_id")]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = string.Empty; // e.g. "MOVE", "LIFT"

    [JsonPropertyName("destination")]
    public string? Destination { get; set; }

    [JsonPropertyName("params")]
    public Dictionary<string, string>? Params { get; set; }
}
