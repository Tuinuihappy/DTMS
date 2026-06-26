namespace DTMS.Facility.Application.Services;

// Vendor action config keyed by intent in StationVendorTarget.Actions
// (e.g. "lift", "drop"). Plain data DTO so the Application layer does
// not need to import the Domain value object.
public sealed record StationActionConfig(
    string ActionType,
    string Category,
    IReadOnlyDictionary<string, string>? Parameters);

// Carries everything Dispatch needs to translate a station into RIOT3 mission(s):
// the vendor-side refs for the MOVE leg, plus an optional action map keyed by
// intent. Dispatch picks the right entry from Actions based on the task type
// (TaskType.Lift → Actions["lift"], etc.). Null/empty Actions = pure MOVE.
public sealed record StationVendorTarget(
    Guid StationId,
    Guid MapId,
    string MapVendorRef,
    string StationVendorRef,
    IReadOnlyDictionary<string, StationActionConfig>? Actions = null);

/// <summary>
/// Station lookup outcome. ManualOverrideActive=true means an operator has forced this station offline.
/// </summary>
public sealed record StationLookupResult(
    Guid Id,
    string? Code,
    bool IsActive,
    bool ManualOverrideActive,
    string? ManualOverrideReason);

/// <summary>
/// Warehouse lookup outcome. Mirrors StationLookupResult shape but for the
/// Warehouse aggregate added in Phase 2.1 (per ADR-002). Consumers see
/// only the read-side projection — no domain types leak.
/// </summary>
public sealed record WarehouseLookupResult(
    Guid Id,
    string Code,
    string Name,
    bool IsActive);

public interface IFacilityReadService
{
    Task<bool> StationExistsAsync(Guid stationId, CancellationToken cancellationToken = default);

    Task<Guid?> ResolveStationByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a station by its vendor-side identifier (e.g. RIOT3 station id).
    /// Returns the DTMS station Guid, or null if no station has the given VendorRef.
    /// Used by vendor webhook handlers where the inbound payload carries the
    /// vendor's numeric station id rather than the DTMS Code — IDs are
    /// case-stable and survive vendor renames, unlike station names.
    /// </summary>
    Task<Guid?> ResolveStationByVendorRefAsync(string vendorRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a mixed list of station GUIDs and codes in at most 2 queries.
    /// Returns a case-insensitive dictionary mapping each input value to its lookup result
    /// (including IsActive + manual override state). Inputs that do not match any station
    /// are omitted from the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, StationLookupResult>> ResolveStationsBatchAsync(
        IReadOnlyList<string> locationCodes,
        CancellationToken cancellationToken = default);

    Task<StationVendorTarget?> GetStationVendorTargetAsync(
        Guid stationId,
        CancellationToken cancellationToken = default);

    Task<double?> GetRouteCostAsync(
        Guid fromStationId,
        Guid toStationId,
        CancellationToken cancellationToken = default);

    // ── Warehouse lookups (Phase 2.6) ────────────────────────────────────
    // Mirror the station lookup shape so consumers (DeliveryOrder validation,
    // Phase 4 operator assignment, Phase 5 fleet provider matching) have
    // the same surface to bind against.

    Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken = default);

    Task<Guid?> ResolveWarehouseByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-resolve warehouse codes in one round trip. Caller passes raw
    /// codes (possibly mixed case); the result is keyed by the caller's
    /// original input. Codes that don't match are omitted — caller decides
    /// if that's an error (order validation = yes; suggestion box = no).
    /// </summary>
    Task<IReadOnlyDictionary<string, WarehouseLookupResult>> ResolveWarehousesBatchAsync(
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken = default);
}
