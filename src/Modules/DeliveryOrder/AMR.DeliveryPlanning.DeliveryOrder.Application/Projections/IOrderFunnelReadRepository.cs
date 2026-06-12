namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

public record OrderFunnelBucketEntry(
    DateTime BucketHour,
    int Confirmed,
    int Dispatched,
    int InProgress,
    int Completed,
    int PartiallyCompleted,
    int Failed,
    int Cancelled,
    int Rejected,
    int Held,
    int Released);

public interface IOrderFunnelReadRepository
{
    /// <summary>
    /// Hour buckets within the window, oldest first. Missing hours are
    /// NOT padded (caller fills zero rows if it needs a contiguous
    /// timeline). Inclusive of fromUtc, exclusive of toUtc.
    /// </summary>
    Task<IReadOnlyList<OrderFunnelBucketEntry>> GetRangeAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
