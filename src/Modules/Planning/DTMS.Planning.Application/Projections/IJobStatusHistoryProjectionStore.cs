namespace DTMS.Planning.Application.Projections;

/// <summary>
/// Write-side abstraction consumed by <c>JobStatusHistoryProjector</c>.
/// Mirrors the same shape as the DeliveryOrder counterpart so both
/// modules host structurally identical projectors — just different
/// aggregates, schemas, and events.
/// </summary>
public interface IJobStatusHistoryProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);

    Task<(string ToStatus, DateTime OccurredAt)?> GetLatestForJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task AppendAsync(
        string projectorName,
        Guid eventId,
        Guid jobId,
        Guid deliveryOrderId,
        string? fromStatus,
        string toStatus,
        DateTime occurredAt,
        string? reason,
        CancellationToken cancellationToken = default);
}
