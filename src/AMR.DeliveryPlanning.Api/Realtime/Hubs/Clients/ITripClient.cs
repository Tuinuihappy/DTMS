namespace AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;

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
}
