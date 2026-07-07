using System.Text.Json;
using System.Text.Json.Serialization;

namespace DTMS.Api.Serialization;

/// <summary>
/// Coerces every <see cref="DateTime"/> deserialized from a JSON request body
/// to UTC. All DTMS persistence uses PostgreSQL <c>timestamp with time zone</c>
/// columns, and Npgsql refuses to write a <see cref="DateTime"/> whose
/// <see cref="DateTime.Kind"/> is Local or Unspecified — surfacing as a 500
/// ("Cannot write DateTime with Kind=Local ... only UTC is supported") on save.
///
/// <para>Source systems send ISO-8601 timestamps that may carry an offset
/// (e.g. <c>+07:00</c>) or none. System.Text.Json binds an offset value to a
/// Local <see cref="DateTime"/> (already adjusted to the host zone), so
/// <see cref="DateTime.ToUniversalTime"/> recovers the correct instant; a bare
/// value is Unspecified and is assumed to already be UTC. Registering this once
/// in <c>ConfigureHttpJsonOptions</c> fixes the whole write surface — actedAt,
/// ServiceWindow, and any future DateTime field — instead of per-field patches.
/// The nullable case is handled by System.Text.Json's built-in
/// <c>Nullable&lt;T&gt;</c> wrapper delegating to this converter.</para>
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ToUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToUtc(value));

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
