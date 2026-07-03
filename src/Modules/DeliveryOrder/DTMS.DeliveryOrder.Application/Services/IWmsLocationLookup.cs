namespace DTMS.DeliveryOrder.Application.Services;

/// <summary>
/// Outcome of a WMS location lookup. Mirrors <see cref="StationLookupResult"/>
/// but for the WMS snapshot — Manual/Fleet transport modes resolve their
/// PickupLocationCode / DropLocationCode through this contract instead of
/// against internal Warehouse rows.
///
/// ParentLocationCode is the zone key (e.g. "LOC-000149" = WIP) that
/// downstream operator-assignment uses to pick an eligible worker.
/// </summary>
public sealed record WmsLocationLookupResult(
    Guid Id,
    int ExternalId,
    string Code,
    string DisplayName,
    bool IsActive,
    string? ParentLocationCode,
    double? Latitude,
    double? Longitude);

public interface IWmsLocationLookup
{
    Task<Guid?> ResolveByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Batch-resolve a mix of location codes in a single round trip. Returns
    /// a case-insensitive map keyed by the caller's original input. Missing
    /// codes are omitted; caller decides whether that's a hard error.
    /// </summary>
    Task<IReadOnlyDictionary<string, WmsLocationLookupResult>> ResolveBatchAsync(
        IReadOnlyList<string> codes,
        CancellationToken ct = default);

    /// <summary>
    /// WMS PR-3 — fetch the full lookup result for a known WMS location Id.
    /// Used by <c>ManualDispatchStrategy</c> to resolve the pickup location's
    /// <see cref="WmsLocationLookupResult.ParentLocationCode"/> (zone key
    /// for operator assignment) + <see cref="WmsLocationLookupResult.Latitude"/>/
    /// <see cref="WmsLocationLookupResult.Longitude"/> (geofence anchor).
    /// Returns null if the Id no longer exists (upstream deletion mid-flight).
    /// </summary>
    Task<WmsLocationLookupResult?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
