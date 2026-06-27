namespace DTMS.Api.Realtime.Hubs.Clients;

/// <summary>
/// Phase 4.6 — Typed SignalR client for <see cref="Hubs.ManualBoardHub"/>.
/// Powers the dispatcher's /dispatch/manual page so override decisions
/// and reassignments land in real time instead of waiting for the 10s
/// poll. Payloads are refetch hints (the page already has a query for
/// the data, broadcast triggers it to re-run).
/// </summary>
public interface IManualBoardClient
{
    /// <summary>
    /// A geofence override decision changed (Approved / Denied / Expired).
    /// Hint shape: <c>{ overrideRequestId, tripId, status }</c>.
    /// </summary>
    Task OverrideDecided(object hint);

    /// <summary>
    /// A Manual trip was reassigned to a different operator.
    /// Hint shape: <c>{ tripId, oldOperatorId, newOperatorId }</c>.
    /// </summary>
    Task TripReassigned(object hint);
}
