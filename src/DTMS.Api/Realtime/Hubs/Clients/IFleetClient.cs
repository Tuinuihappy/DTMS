namespace DTMS.Api.Realtime.Hubs.Clients;

/// <summary>
/// Typed SignalR client for <see cref="FleetHub"/>. Powers the live
/// facility map — robot positions arrive in batches throttled by the
/// <c>FleetPositionThrottler</c> to one push per second per facility
/// regardless of upstream RIOT3 webhook rate.
/// </summary>
public interface IFleetClient
{
    /// <summary>
    /// Batch of robot positions for a given facility (the group key the
    /// browser subscribed to). Browser merges into the map by robotId.
    /// </summary>
    Task RobotPositionsUpdated(IReadOnlyList<object> positions);

    /// <summary>
    /// A robot's state transitioned (Idle/Busy/Offline/etc.). Always
    /// pushed individually — state changes are rare enough that batching
    /// doesn't pay off.
    /// </summary>
    Task RobotStateChanged(Guid robotId, string state);
}
