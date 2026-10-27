namespace DTMS.Facility.Application.Services;

/// <summary>
/// Which stations other modules still need — an active OrderTemplate routes
/// through it, an unfinished trip goes to it, or an unfinished order's item
/// points at it. Purge refuses those. Implemented in the API composition
/// root (Facility doesn't reference Planning / Dispatch / DeliveryOrder).
/// </summary>
public interface IStationUsageReader
{
    /// <summary>Station id → why it is still in use. Unused stations are absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> FindInUseAsync(
        IReadOnlyCollection<Guid> stationIds, CancellationToken cancellationToken = default);
}
