namespace DTMS.Facility.Application.Services;

/// <summary>
/// VendorAdapter:Riot3:MapStationSync settings the sync handler needs.
/// </summary>
public sealed class StationSyncOptions
{
    /// <summary>
    /// Share of a map's active stations one sync may deactivate on its own.
    /// More than that is held until someone confirms it from the UI — a RIOT3
    /// map mid-edit or a bad answer shouldn't take a large part of the map
    /// out of service silently. Default 0.2 (20%).
    /// </summary>
    public double MaxDeactivateFraction { get; init; } = 0.2;
}
