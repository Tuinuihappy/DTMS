using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;

public sealed record OmsLot(
    [property: JsonPropertyName("lotNo")] string LotNo);
