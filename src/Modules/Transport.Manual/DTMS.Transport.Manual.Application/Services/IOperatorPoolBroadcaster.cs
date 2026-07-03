using DTMS.Transport.Manual.Application.Queries.GetPoolTrips;

namespace DTMS.Transport.Manual.Application.Services;

// Well-known label values emitted on the dtms.pool.claim.total counter.
// Kept close to the broadcaster contract so the metric taxonomy is
// obvious to anyone tracing a Grafana panel back to code.
public static class PoolClaimOutcomes
{
    public const string Success = "success";
    public const string Conflict = "conflict";   // 409 / CAS lost / different operator
    public const string Error = "error";         // any other Result.Failure
}

/// <summary>
/// Metric sink for the operator pool. Application-side handlers call
/// this via DI to record outcomes without depending on Api-layer types
/// (module boundary — DTMS.Api implements the concrete sink; Application
/// only sees this interface).
/// </summary>
public interface IPoolMetricsSink
{
    /// <summary>
    /// Record a claim attempt. <paramref name="outcome"/> is a
    /// <see cref="PoolClaimOutcomes"/> constant. When the outcome is
    /// <c>Success</c>, pass the trip's dispatch time so wait-in-pool
    /// can be recorded on the same call.
    /// </summary>
    void RecordClaim(string outcome, double latencyMs, DateTime? dispatchedAt = null);
}

// WMS PR-4b (PR-D) — Realtime pool broadcast contract.
//
// Fired at three points in the pool lifecycle:
//   • After ManualDispatchStrategy pool-path save          → BroadcastAdded
//   • After AcknowledgeTripCommandHandler wins CAS + saves → BroadcastClaimed
//   • After CancelPoolTripCommand (PR-G) commits           → BroadcastRemoved
//
// Every call is fire-and-forget from the caller's perspective — the
// implementation swallows exceptions (broadcast failure must NOT block or
// roll back the DB save that just committed). Clients degrade to REST
// refetch on their next reconnect if the broadcast never lands.
public interface IOperatorPoolBroadcaster
{
    /// <summary>
    /// A trip just entered the pool. <paramref name="dto"/> is the same
    /// shape the REST list endpoint returns so the frontend reducer can
    /// insert it without a shape translation.
    /// </summary>
    Task BroadcastAddedAsync(PoolTripDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// A trip left the pool because <paramref name="operatorId"/> won the
    /// CAS. <paramref name="operatorName"/> is the operator DisplayName —
    /// non-winning clients render a brief "claimed by X" toast before
    /// removing the card.
    /// </summary>
    Task BroadcastClaimedAsync(Guid tripId, Guid operatorId, string operatorName,
        DateTime claimedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// A pooled trip was cancelled or otherwise removed before any
    /// operator claimed it. Clients drop the card silently.
    /// </summary>
    Task BroadcastRemovedAsync(Guid tripId, string reason, CancellationToken cancellationToken = default);
}
