namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P3 — abstraction so dashboard-feeding projectors (OrderFunnel
/// today, more to follow) can push a "data changed" hint to the
/// composition-root SignalR layer without taking a dependency on
/// MassTransit's SignalR types or the Api project.
///
/// <para>The composition-root implementation enqueues each hint onto the
/// existing <c>DashboardCounterBatcher</c> (P0.B11) — the batcher drains
/// every 250 ms and pushes one <c>CountersUpdated</c> per board so the
/// browser's chart re-render rate stays bounded under bursts.</para>
///
/// <para>Frontend treats the hint as a refetch trigger (debounced) rather
/// than applying deltas client-side. Avoids drift between projection
/// state and rendered chart at the cost of one extra REST roundtrip per
/// batch window. Acceptable for the dashboard query patterns (each one is
/// a single Postgres GROUP BY against a hot-indexed table).</para>
/// </summary>
public interface IDashboardRealtimePublisher
{
    /// <summary>
    /// Hint that the order funnel projection for the given hour bucket
    /// has new data. Should be called AFTER the projection store write
    /// succeeds.
    /// </summary>
    Task PublishOrderFunnelUpdatedAsync(DateTime bucketHourUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default null implementation — keeps the projector working when no
/// realtime layer is wired (tests, alternative compositions).
/// </summary>
public sealed class NoopDashboardRealtimePublisher : IDashboardRealtimePublisher
{
    public Task PublishOrderFunnelUpdatedAsync(
        DateTime bucketHourUtc, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
