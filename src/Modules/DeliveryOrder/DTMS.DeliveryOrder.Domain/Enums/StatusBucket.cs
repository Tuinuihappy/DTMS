namespace DTMS.DeliveryOrder.Domain.Enums;

/// <summary>
/// Higher-level grouping of <see cref="OrderStatus"/> values used by query
/// APIs and dashboards. Buckets exist so callers can ask for "anything
/// currently moving through the pipeline" or "anything terminal" without
/// having to spell out 5–7 enum values.
///
/// Definitions live with the domain — the same membership feeds the stats
/// aggregator, the orders list filter, and any future report — so a new
/// status added to the system (e.g. Quarantined) only needs to be bucketed
/// here once.
/// </summary>
public enum StatusBucket
{
    /// <summary>In the pipeline: submitted but not yet finished or terminated.</summary>
    Active,
    /// <summary>Reached a successful end-state (fully or partially delivered).</summary>
    Completed,
    /// <summary>Reached a non-success end-state (held, failed, cancelled, rejected, amended).</summary>
    Terminal,
}

public static class OrderStatusBuckets
{
    public static readonly OrderStatus[] Active =
    {
        OrderStatus.Submitted,
        OrderStatus.Validated,
        OrderStatus.Confirmed,
        OrderStatus.Planning,
        OrderStatus.Planned,
        OrderStatus.Dispatched,
        OrderStatus.InProgress,
    };

    public static readonly OrderStatus[] Completed =
    {
        OrderStatus.Completed,
        OrderStatus.PartiallyCompleted,
    };

    public static readonly OrderStatus[] Terminal =
    {
        OrderStatus.Held,
        OrderStatus.Failed,
        OrderStatus.Cancelled,
        OrderStatus.Rejected,
    };

    public static OrderStatus[] For(StatusBucket bucket) => bucket switch
    {
        StatusBucket.Active => Active,
        StatusBucket.Completed => Completed,
        StatusBucket.Terminal => Terminal,
        _ => Array.Empty<OrderStatus>(),
    };
}
