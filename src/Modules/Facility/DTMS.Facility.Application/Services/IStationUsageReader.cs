namespace DTMS.Facility.Application.Services;

/// <summary>
/// Which stations other modules still need — an active OrderTemplate routes
/// through it or MOVEs to it, an unfinished trip goes to it, or an unfinished
/// order's item points at it. Purge refuses those. Implemented in the API
/// composition root (Facility doesn't reference Planning / Dispatch / DeliveryOrder).
/// </summary>
public interface IStationUsageReader
{
    /// <summary>Station id → why it is still in use. Unused stations are absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> FindInUseAsync(
        IReadOnlyCollection<StationUsageCandidate> stations, CancellationToken cancellationToken = default);
}

/// <summary>
/// A station to check, by both keys other modules hold: its DTMS id (routes,
/// trips, orders) and its RIOT3 map + station id (template MOVE missions).
/// </summary>
public sealed record StationUsageCandidate(Guid Id, int Riot3MapId, int? Riot3StationId);
