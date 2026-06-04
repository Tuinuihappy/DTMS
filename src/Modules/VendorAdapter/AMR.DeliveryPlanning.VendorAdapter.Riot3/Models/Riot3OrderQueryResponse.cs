using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

// Slim projection of the RIOT3 GET /api/v4/orders/{key}?isUpper=true response
// used by the reconciliation poller. Only fields the reconciler maps to
// Trip transitions are surfaced — RIOT3 returns the full order echo
// (missions, vehicle hints, etc.) which we ignore here.
public sealed class Riot3OrderQueryResponse
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("data")]
    public Riot3OrderQueryData? Data { get; init; }
}

public sealed class Riot3OrderQueryData
{
    [JsonPropertyName("orderKey")]
    public string? OrderKey { get; init; }

    [JsonPropertyName("upperKey")]
    public string? UpperKey { get; init; }

    // Task-level state — same vocabulary as the notify payload's task.state
    // (PROCESSING / FINISHED / FAILED / CANCELED / QUEUEING / HANG / HELD …).
    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("failReason")]
    public Riot3FailResult? FailReason { get; init; }

    [JsonPropertyName("cancelReason")]
    public string? CancelReason { get; init; }

    [JsonPropertyName("processingVehicle")]
    public Riot3NotifyProcessingVehicle? ProcessingVehicle { get; init; }
}
