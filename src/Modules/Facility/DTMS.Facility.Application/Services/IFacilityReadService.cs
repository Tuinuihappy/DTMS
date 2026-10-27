namespace DTMS.Facility.Application.Services;

/// <summary>
/// Station lookup outcome. ManualOverrideActive=true means an operator has forced this station offline.
/// </summary>
public sealed record StationLookupResult(
    Guid Id,
    string? Code,
    bool IsActive,
    bool ManualOverrideActive,
    string? ManualOverrideReason);

/// <summary>A RIOT3 (map id, station id) pair, as an OrderTemplate MOVE mission carries it.</summary>
public sealed record VendorStationRef(string MapVendorRef, string StationVendorRef);

/// <summary>
/// A referenced RIOT3 station the sync says no longer exists: its row is
/// inactive (Name known) or the map is synced but has no such station (Name null).
/// </summary>
public sealed record UnavailableVendorStation(string MapVendorRef, string StationVendorRef, string? Name);

public interface IFacilityReadService
{
    /// <summary>
    /// The subset of <paramref name="refs"/> that RIOT3 no longer has, judged by
    /// the station sync. Refs on a map DTMS doesn't sync are never reported —
    /// DTMS can't tell, so it doesn't block on them.
    /// </summary>
    Task<IReadOnlyList<UnavailableVendorStation>> FindUnavailableVendorStationsAsync(
        IReadOnlyCollection<VendorStationRef> refs,
        CancellationToken cancellationToken = default);

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
}
