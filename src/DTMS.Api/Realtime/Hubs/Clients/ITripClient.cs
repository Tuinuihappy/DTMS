namespace DTMS.Api.Realtime.Hubs.Clients;

/// <summary>
/// Typed SignalR client for <see cref="TripHub"/>. Used by Trip detail
/// drawers + the Trip status timeline (P1).
/// </summary>
public interface ITripClient
{
    /// <summary>
    /// New timeline entry for a Trip (Phase P1 status history).
    /// </summary>
    Task TimelineUpdated(object entry);

    /// <summary>
    /// Trip's overall <c>TripStatus</c> changed
    /// (Created/InProgress/Paused/Completed/Failed/Cancelled).
    /// </summary>
    Task StatusChanged(object change);

    /// <summary>
    /// Mission-level progress within an InProgress trip — pickup arrived,
    /// drop completed, etc. Used by the operator's live trip detail view.
    /// </summary>
    Task MissionUpdated(object missionEvent);

    /// <summary>
    /// Cross-trip list hint pushed to the <c>trips-list</c> group whenever
    /// any trip in the queue changes status. Payload is a refetch hint
    /// (<c>{ tripId, toStatus }</c>), not a full row — the list page
    /// debounces and re-fetches the current page slice. Mirrors
    /// <c>IOrderClient.ListItemUpdated</c>.
    /// </summary>
    Task ListItemUpdated(object hint);
}
