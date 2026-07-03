namespace DTMS.Api.Realtime.Hubs.Clients;

/// <summary>
/// Typed SignalR client for <see cref="Hubs.OperatorPoolHub"/>. All
/// connected operator PWAs receive the same three events so the local
/// pool list stays in lock-step with the server side of the CAS.
///
/// Payloads are typed <c>object</c> (server sends anonymous DTOs) — the
/// TypeScript client re-types on the wire so both sides evolve without a
/// shared contract library.
/// </summary>
public interface IOperatorPoolClient
{
    /// <summary>
    /// A trip just entered the pool. Payload is a <c>PoolTripDto</c>
    /// (see GetPoolTripsQuery.PoolTripDto) — the client inserts it into
    /// its FIFO-ordered list at the position implied by <c>DispatchedAt</c>.
    /// </summary>
    Task PoolTripAdded(object trip);

    /// <summary>
    /// A trip left the pool because an operator won the CAS. Payload:
    /// <c>{ tripId, claimedByOperatorId, claimedByName, claimedAt }</c>.
    /// Every non-winning client removes the trip from its local list.
    /// The winning client also receives this (idempotent — the trip
    /// was already removed optimistically on their own 204 response).
    /// </summary>
    Task PoolTripClaimed(object claim);

    /// <summary>
    /// A pooled trip was cancelled or otherwise removed before any
    /// operator claimed it. Payload: <c>{ tripId, reason }</c>. Clients
    /// remove the trip from the list; no navigation happens.
    /// </summary>
    Task PoolTripRemoved(object removal);
}
