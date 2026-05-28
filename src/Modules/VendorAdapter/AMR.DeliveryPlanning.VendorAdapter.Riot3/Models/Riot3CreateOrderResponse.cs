using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

// Slim projection of the RIOT3 POST /api/v4/orders response — we only need
// the orderKey so the caller can correlate later callbacks. RIOT3 returns
// much more data (full mission echo, vehicle hints, etc.) but we don't
// consume it here.
public sealed class Riot3CreateOrderResponse
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("data")]
    public Riot3CreateOrderResponseData? Data { get; init; }
}

public sealed class Riot3CreateOrderResponseData
{
    [JsonPropertyName("orderKey")]
    public string? OrderKey { get; init; }

    [JsonPropertyName("upperKey")]
    public string? UpperKey { get; init; }
}
