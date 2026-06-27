namespace DTMS.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P3 — write-side abstraction consumed by
/// <c>OrderFunnelProjector</c>. Upserts the hour-bucket row + increments
/// the matching status column atomically.
/// </summary>
public interface IOrderFunnelProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increment the counter for <paramref name="status"/> within the
    /// hour bucket that contains <paramref name="occurredOn"/>. Creates
    /// the row if it doesn't exist. Records the inbox entry in the same
    /// transaction so retries are idempotent.
    /// </summary>
    Task IncrementAsync(
        string projectorName,
        Guid eventId,
        DateTime occurredOn,
        string status,
        CancellationToken cancellationToken = default);
}
