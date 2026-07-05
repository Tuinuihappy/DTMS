using DTMS.Dispatch.Domain.Entities;

namespace DTMS.Dispatch.Domain.Repositories;

public interface ITripRepository
{
    Task<Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up a trip by the DTMS-side correlation key (UpperKey) RIOT3
    /// echoes back on every webhook.
    /// </summary>
    Task<Trip?> GetByUpperKeyAsync(string upperKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up a trip by the vendor-side order key RIOT3 assigns at dispatch
    /// (Trip.VendorOrderKey). RIOT3's sub-task webhooks omit the parent
    /// `task` object — so UpperKey isn't carried — but `subTask.taskKey`
    /// carries the vendor's order key, which lets us correlate back to the
    /// owning Trip.
    /// </summary>
    Task<Trip?> GetByVendorOrderKeyAsync(string vendorOrderKey, CancellationToken cancellationToken = default);

    Task<List<Trip>> GetActiveTripsByVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists envelope-dispatched trips still awaiting a terminal vendor
    /// callback (Status ∈ {Created, InProgress, Paused}). Used by the
    /// reconciliation poller to backfill missed webhooks. Trips older than
    /// <paramref name="staleCutoffUtc"/> are excluded — past that age the
    /// poller stops chasing them and ops takes over.
    /// </summary>
    Task<List<Trip>> GetInFlightEnvelopeTripsAsync(DateTime staleCutoffUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Self-heal backstop for the reconciler. Lists terminal envelope trips
    /// (Status ∈ {Completed, Failed}) that finished recently but never
    /// captured a vendor vehicle — the TASK_PROCESSING signal was missed AND
    /// a webhook (not the reconciler) drove the terminal transition, so the
    /// in-flight loop never ran the terminal snapshot/backfill pass on them.
    ///
    /// Gated on <c>VendorFinalSnapshot IS NULL</c>: the caller fetches the
    /// vendor record, captures the snapshot, and backfills the vehicle in one
    /// pass — after which the snapshot is non-null and the trip permanently
    /// drops out of this query (no re-fetch loop, even when the vendor record
    /// genuinely carries no vehicle). <paramref name="completedSinceUtc"/>
    /// bounds the sweep to recent completions.
    /// </summary>
    Task<List<Trip>> GetTerminalTripsMissingVehicleAsync(DateTime completedSinceUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every Trip belonging to a DeliveryOrder, regardless of
    /// status. Used by the cancel-cascade flow to find which trips to
    /// stop on the vendor side.
    /// </summary>
    Task<List<Trip>> GetByDeliveryOrderIdAsync(Guid deliveryOrderId, CancellationToken cancellationToken = default);

    Task AddAsync(Trip trip, CancellationToken cancellationToken = default);
    Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default);

    /// <summary>
    /// Walk back PreviousAttemptId chain to find the first attempt's Id.
    /// Used as the stable shipmentId for upstream OMS notifications so
    /// retries don't appear as new shipments to OMS. Returns the input
    /// tripId itself if the chain root isn't found (broken data) — safer
    /// than throwing for an observability-only feature.
    /// </summary>
    Task<Guid> GetRootTripIdAsync(Guid tripId, CancellationToken cancellationToken = default);

    /// <summary>
    /// WMS PR-4b — Atomic pool-claim CAS. Attempts to bind
    /// <paramref name="operatorId"/> to a pooled trip in a single UPDATE
    /// (no read-then-write race window). Wins only when the trip still
    /// matches the pool predicate:
    ///   Status = 'Created' ∧ DispatchedAt IS NOT NULL ∧
    ///   ClaimedByOperatorId IS NULL.
    /// Sets <c>ClaimedByOperatorId</c> + <c>ClaimedAt</c>; status stays
    /// <see cref="TripStatus.Created"/> — the Created → InProgress
    /// transition + <c>TripStartedDomainEvent</c> emit are done via the
    /// domain aggregate in the caller after this returns <c>true</c>.
    /// Returns <c>false</c> when someone else already claimed the trip,
    /// the trip has already started, or the trip never entered the pool
    /// (AMR trip). The caller maps <c>false</c> to HTTP 409 so the PWA
    /// toasts + refreshes.
    /// </summary>
    Task<bool> TryClaimFromPoolAsync(Guid tripId, Guid operatorId, CancellationToken cancellationToken = default);
}
