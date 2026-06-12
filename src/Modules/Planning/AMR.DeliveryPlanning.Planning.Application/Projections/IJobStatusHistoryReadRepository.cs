namespace AMR.DeliveryPlanning.Planning.Application.Projections;

public record JobStatusHistoryEntry(
    Guid EventId,
    Guid JobId,
    Guid DeliveryOrderId,
    string? FromStatus,
    string ToStatus,
    DateTime OccurredAt,
    string? Reason);

public interface IJobStatusHistoryReadRepository
{
    /// <summary>
    /// All transitions for a job, newest first. Empty for unknown or
    /// pre-projection jobs (use backfill SQL to seed).
    /// </summary>
    Task<IReadOnlyList<JobStatusHistoryEntry>> GetForJobAsync(
        Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// All transitions for every job owned by a delivery order, newest
    /// first. Powers the per-order rollup view (multi-group orders have
    /// one job per group, so this returns the union sorted by time).
    /// </summary>
    Task<IReadOnlyList<JobStatusHistoryEntry>> GetForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default);
}
