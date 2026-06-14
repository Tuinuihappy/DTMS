namespace AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;

/// <summary>
/// Typed SignalR client for <see cref="DashboardHub"/>. Pushed by P3
/// projectors via the 250 ms <c>DashboardCounterBatcher</c> so chart
/// re-renders are bounded to ~4 Hz even under bursty status traffic.
/// </summary>
public interface IDashboardClient
{
    /// <summary>
    /// One or more aggregate counter deltas (status counts, funnel cells,
    /// utilization gauges). Sent as a batch so the UI updates in one
    /// React render tick.
    /// </summary>
    Task CountersUpdated(IReadOnlyList<object> deltas);

    /// <summary>
    /// Full KPI snapshot — used after reconnect or on demand. Less
    /// frequent than <see cref="CountersUpdated"/> so the wire cost is
    /// acceptable as full payload.
    /// </summary>
    Task KpiSnapshotUpdated(object snapshot);
}
