using System.Text.Json.Serialization;

namespace DTMS.OmsAdapter.Abstractions.Models;

public sealed record OmsLot(
    [property: JsonPropertyName("lotNo")] string LotNo);
