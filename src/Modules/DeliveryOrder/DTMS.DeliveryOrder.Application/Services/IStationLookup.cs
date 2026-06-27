namespace DTMS.DeliveryOrder.Application.Services;

/// <summary>
/// Outcome of a station lookup. ManualOverrideActive=true means an operator has forced this station offline
/// (independent of RIOT3 sync) and any in-flight order referencing it should be rejected.
/// </summary>
public sealed record StationLookupResult(
    Guid Id,
    string? Code,
    bool IsActive,
    bool ManualOverrideActive,
    string? ManualOverrideReason);

public interface IStationLookup
{
    Task<bool> ExistsAsync(Guid stationId, CancellationToken ct = default);
    Task<Guid?> ResolveByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, StationLookupResult>> ResolveBatchAsync(IReadOnlyList<string> locationCodes, CancellationToken ct = default);
}
