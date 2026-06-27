namespace DTMS.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P1 write-side abstraction consumed by
/// <c>OrderStatusHistoryProjector</c>. Bundles three operations the
/// projector needs into one interface so the Application layer doesn't
/// reach into Infrastructure for the DbContext directly. The
/// Infrastructure implementation backs all three with the same
/// <c>DeliveryOrderDbContext</c> instance, so <see cref="AppendAsync"/>
/// commits the inbox row + history row in one transaction.
/// </summary>
public interface IOrderStatusHistoryProjectionStore
{
    /// <summary>
    /// Idempotency check — returns true when this projector has already
    /// processed the given event id (inbox row exists).
    /// </summary>
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Most recent transition recorded for the order, or null if none.
    /// Caller uses the returned ToStatus as the new row's FromStatus and
    /// compares OccurredAt to detect out-of-order events.
    /// </summary>
    Task<(string ToStatus, DateTime OccurredAt)?> GetLatestForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the history row + inbox row + SaveChanges atomically.
    /// </summary>
    Task AppendAsync(
        string projectorName,
        Guid eventId,
        Guid orderId,
        string? fromStatus,
        string toStatus,
        DateTime occurredAt,
        string? reason,
        CancellationToken cancellationToken = default);
}
