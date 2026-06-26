using System.Text.Json.Serialization;

namespace DTMS.Transport.Amr.Models;

// Matches RIOT3.0 API v4 "Robot Operation" — POST /api/v4/robots/operation
// Body shape: { "vehicles": [{ "key": "<deviceKey>" }], "operation": "PASS" }
// Used for operator-acknowledged checkpoint passes; distinct from
// order-level commands which target the orderKey, not the deviceKey.
public class Riot3RobotOperationRequest
{
    [JsonPropertyName("vehicles")]
    public List<Riot3VehicleKey> Vehicles { get; set; } = new();

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;
}

public class Riot3VehicleKey
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;
}

public static class Riot3RobotOperationType
{
    // Operator confirms robot may proceed past a waiting checkpoint
    // (e.g. acknowledging that goods are physically loaded at pickup).
    public const string Pass = "PASS";
}
