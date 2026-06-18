using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

// Accepts a JSON token that is either a string or a number (or null) and
// surfaces it as a string. RIOT3 inconsistently types result/error codes —
// numeric on the success path, string on the failure path — and we'd
// rather not lose vendor state to a deserialization throw mid-poll.
internal sealed class StringOrNumberJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var i) => i.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for string-or-number field."),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
