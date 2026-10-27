namespace DTMS.Planning.Application.Services;

/// <summary>A MOVE target as RIOT3 sees it: map id + station id.</summary>
public sealed record VendorStation(int MapId, int StationId);

/// <summary>A MOVE target RIOT3 no longer has. Name is null when DTMS never knew it.</summary>
public sealed record RemovedVendorStation(int MapId, int StationId, string? Name);

/// <summary>
/// Answers "does RIOT3 still have these stations?" from the Facility station
/// sync, so a template pointing at a removed station is refused by DTMS —
/// with the template and station named — instead of being POSTed to RIOT3.
/// Implemented in the API composition root (Planning doesn't reference
/// Facility.Application).
/// </summary>
public interface IVendorStationAvailability
{
    Task<IReadOnlyList<RemovedVendorStation>> FindRemovedAsync(
        IReadOnlyCollection<VendorStation> stations, CancellationToken cancellationToken = default);
}
