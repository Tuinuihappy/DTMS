using System.Text.Json.Serialization;

namespace DTMS.Wms.Application.Services;

/// <summary>
/// Paginated envelope returned by <c>GET /location</c>. Field names match the
/// external WMS JSON so <see cref="System.Text.Json.JsonSerializer"/> can
/// deserialize without a custom converter.
/// </summary>
public sealed class WmsLocationPage
{
    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }

    [JsonPropertyName("data")]
    public IReadOnlyList<WmsLocationDto> Data { get; init; } = Array.Empty<WmsLocationDto>();
}

/// <summary>
/// Single row from the external WMS location listing. Property names
/// preserve upstream casing / typos (e.g. <c>zGpsHeigth</c>) via
/// <see cref="JsonPropertyName"/> — DTMS renames on persistence.
/// </summary>
public sealed class WmsLocationDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("locationCode")]
    public string LocationCode { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("type")]
    public int Type { get; init; }

    [JsonPropertyName("typeName")]
    public string? TypeName { get; init; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; }

    [JsonPropertyName("isStorageLocation")]
    public bool IsStorageLocation { get; init; }

    [JsonPropertyName("parentLocationId")]
    public int? ParentLocationId { get; init; }

    [JsonPropertyName("parentLocationCode")]
    public string? ParentLocationCode { get; init; }

    [JsonPropertyName("parentLocationDisplayName")]
    public string? ParentLocationDisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; init; }

    // Upstream typo — kept on the wire so deserialization succeeds; the
    // domain renames it to ZGpsHeight.
    [JsonPropertyName("zGpsHeigth")]
    public double? ZGpsHeight { get; init; }

    [JsonPropertyName("zTolerance")]
    public double? ZTolerance { get; init; }

    [JsonPropertyName("accuracy")]
    public double? Accuracy { get; init; }

    // Upstream typo preserved on wire.
    [JsonPropertyName("heigthDiff")]
    public double? HeightDiff { get; init; }
}
