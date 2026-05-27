namespace AMR.DeliveryPlanning.Facility.Application.Services;

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
