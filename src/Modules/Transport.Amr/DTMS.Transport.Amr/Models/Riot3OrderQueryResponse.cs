using System.Text.Json;
using System.Text.Json.Serialization;

namespace DTMS.Transport.Amr.Models;

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

    // Order-level state from GET /orders/{key}?isUpper=true. RIOT3 emits it
    // as "orderState" here (not "state" — that field exists only per-mission
    // inside data.missions[]). Vocabulary matches the notify payload's
    // task.state: PROCESSING / FINISHED / FAILED / CANCELLED / HANG / HELD …
    [JsonPropertyName("orderState")]
    public string? State { get; init; }

    [JsonPropertyName("failReason")]
    public Riot3FailResult? FailReason { get; init; }

    [JsonPropertyName("cancelReason")]
    public string? CancelReason { get; init; }

    [JsonPropertyName("processingVehicle")]
    public Riot3NotifyProcessingVehicle? ProcessingVehicle { get; init; }

    // Terminal-record vehicle. RIOT3 emits `processingVehicle` only while the
    // order is running; once it finishes the executing robot is echoed here
    // instead (executeVehicleKey/Name — same key as the earlier
    // processingVehicle.key). Surfaced so the reconciler can capture the
    // robot on a trip whose PROCESSING signal was missed and only the
    // terminal state remains. See ResolvedVehicle for the fallback order.
    [JsonPropertyName("executeVehicleKey")]
    public string? ExecuteVehicleKey { get; init; }

    [JsonPropertyName("executeVehicleName")]
    public string? ExecuteVehicleName { get; init; }

    // Single source of truth for "which robot is/was on this order", used by
    // every capture site (reconciler terminal + self-heal). Prefers the live
    // processingVehicle when present (running order); falls back to the
    // terminal executeVehicle* (finished order). Defined once here so the
    // fallback order can't drift across call sites.
    [JsonIgnore]
    public (string? Key, string? Name) ResolvedVehicle =>
        (ProcessingVehicle?.Key ?? ExecuteVehicleKey,
         ProcessingVehicle?.Name ?? ExecuteVehicleName);

    [JsonPropertyName("orderStateChangeTime")]
    public string? OrderStateChangeTime { get; init; }

    [JsonPropertyName("finalTime")]
    public string? FinalTime { get; init; }

    [JsonPropertyName("missions")]
    public List<Riot3OrderMission>? Missions { get; init; }
}

public sealed class Riot3OrderMission
{
    [JsonPropertyName("missionKey")]
    public string? MissionKey { get; init; }

    [JsonPropertyName("missionIndex")]
    public int? MissionIndex { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("stationName")]
    public string? StationName { get; init; }

    [JsonPropertyName("stationId")]
    public int? StationId { get; init; }

    [JsonPropertyName("actionName")]
    public string? ActionName { get; init; }

    [JsonPropertyName("actionType")]
    public string? ActionType { get; init; }

    // RIOT3 emits resultCode as a JSON number on success (e.g. 0) and as a
    // string on failure (e.g. "E700001"). The tolerant converter normalizes
    // both to string so downstream callers don't have to care which arrived.
    [JsonPropertyName("resultCode")]
    [JsonConverter(typeof(StringOrNumberJsonConverter))]
    public string? ResultCode { get; init; }

    [JsonPropertyName("resultStr")]
    public string? ResultStr { get; init; }

    [JsonPropertyName("changeStateTime")]
    public string? ChangeStateTime { get; init; }
}
