using DTMS.Dispatch.Domain.Entities;

namespace DTMS.Dispatch.Domain.Repositories;

/// <summary>
/// Per-mission audit events. Append-only from the caller's perspective —
/// duplicate (TripId, MissionKey, State) tuples are silently no-op'd by
/// the unique constraint so webhook + reconciler can both write without
/// coordination. Reads serve the trip detail UI and compliance queries.
/// </summary>
public interface ITripMissionEventRepository
{
    /// <summary>
    /// Adds a mission event if one with the same (TripId, MissionKey,
    /// State) doesn't already exist. Returns true when a new row was
    /// persisted, false when the duplicate guard skipped it.
    /// </summary>
    Task<bool> AddIfNotExistsAsync(TripMissionEvent missionEvent, CancellationToken cancellationToken = default);

    Task<List<TripMissionEvent>> GetByTripIdAsync(Guid tripId, CancellationToken cancellationToken = default);
}
