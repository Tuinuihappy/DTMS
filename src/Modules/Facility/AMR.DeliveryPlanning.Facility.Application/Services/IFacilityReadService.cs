namespace AMR.DeliveryPlanning.Facility.Application.Services;

public sealed record StationVendorTarget(
    Guid StationId,
    Guid MapId,
    string MapVendorRef,
    string StationVendorRef);

public interface IFacilityReadService
{
    Task<bool> StationExistsAsync(Guid stationId, CancellationToken cancellationToken = default);

    Task<Guid?> ResolveStationByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a mixed list of station GUIDs and codes in at most 2 queries.
    /// Returns a case-insensitive dictionary mapping each input value to its StationId.
    /// Inputs that do not match any station are omitted from the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, Guid>> ResolveStationsBatchAsync(
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
