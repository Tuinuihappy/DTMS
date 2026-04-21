using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

public class RiotStatusPayload
{
    [JsonPropertyName("robot_id")]
    public string RobotId { get; set; } = string.Empty;

    [JsonPropertyName("battery")]
    public double Battery { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty; // e.g. "idle", "running", "error"

    [JsonPropertyName("current_node")]
    public string? CurrentNode { get; set; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }
}
