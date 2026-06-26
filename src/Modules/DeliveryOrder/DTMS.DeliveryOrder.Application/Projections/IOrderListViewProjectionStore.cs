namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P4 — Mutation abstraction for the OrderListView projection.
///
/// Phase P4.6 (2026-06-25) — every Order lifecycle event now triggers
/// <see cref="RefreshFromAggregateAsync"/>; per-event patch methods are
/// gone. Aggregate is the source of truth, events are just triggers.
/// Trip/Job derived flags retain dedicated setters because they are NOT
/// in the aggregate — they are computed per-event from the cross-module
/// Trip/Job streams.
/// </summary>
public interface IOrderListViewProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);

    Task SetTripDerivedFieldsAsync(Guid orderId, bool hasFailedTrip, Guid? latestTripId, CancellationToken cancellationToken = default);

    Task SetJobDerivedFieldsAsync(Guid orderId, bool hasActiveJob, string? latestJobStatus, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authoritative refresh from the DeliveryOrder aggregate. Loads the
    /// current aggregate state (+ items for SearchText) and rewrites every
    /// display column on the projection row. Trip/Job derived flags are
    /// preserved (those are owned by the dedicated setters above).
    /// Idempotent — replaying the same event recomputes the row identically.
    /// </summary>
    Task RefreshFromAggregateAsync(Guid orderId, DateTime occurredAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 3 — safety-net rebuild. Reconstructs every row from the
    /// canonical sources (DeliveryOrders + Items + dispatch.Trips +
    /// planning.Jobs) using a single transactional UPSERT and DELETEs
    /// orphan rows. Used when the projector falls behind permanently
    /// (swallowed failure), after schema migrations that add columns,
    /// or when a dev wants to reset projection drift locally.
    /// Returns rows upserted + orphans deleted.
    /// </summary>
    Task<ProjectionRebuildResult> RebuildAllAsync(CancellationToken cancellationToken = default);
}

public sealed record ProjectionRebuildResult(int RowsUpserted, int OrphansDeleted, TimeSpan Duration);
