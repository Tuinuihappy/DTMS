namespace AMR.DeliveryPlanning.Facility.Application.Services;

// Carries everything Dispatch needs to translate a station into RIOT3 mission(s):
// the vendor-side refs for the MOVE leg, plus the optional ACT configuration
// (action type/category/params) when the station is more than a pure waypoint.
// ActionType=null means "MOVE only" — do not append an ACT mission.
public sealed record StationVendorTarget(
    Guid StationId,
    Guid MapId,
    string MapVendorRef,
    string StationVendorRef,
    string? ActionType = null,
    string? ActionCategory = null,
    IReadOnlyDictionary<string, string>? ActionParameters = null);

/// <summary>
/// Station lookup outcome. ManualOverrideActive=true means an operator has forced this station offline.
/// </summary>
public sealed record StationLookupResult(
    Guid Id,
    string? Code,
    bool IsActive,
    bool ManualOverrideActive,
    string? ManualOverrideReason);

public interface IFacilityReadService
{
    Task<bool> StationExistsAsync(Guid stationId, CancellationToken cancellationToken = default);

    Task<Guid?> ResolveStationByCodeAsync(string code, CancellationToken cancellationToken = default);

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
}
