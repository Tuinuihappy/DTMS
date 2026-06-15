using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;

namespace AMR.DeliveryPlanning.Dispatch.Application.Projections;

/// <summary>
/// Phase P5.3 — Write-side abstraction for the dispatch.TripItems read
/// model. Combines per-event idempotency (via ProjectionInbox) with the
/// projection writes in a single transaction.
///
/// The projector calls <c>InsertBindingsAsync</c> once per
/// TripStarted event, then <c>UpdateItemStatusForTripAsync</c> on each
/// terminal Trip event (Completed/Failed/Cancelled).
/// </summary>
public interface ITripItemsProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert one row per item snapshot, all bound to the same TripId.
    /// Idempotent via inbox dedup before this is called. ON CONFLICT
    /// (TripId, ItemPk) is a no-op so a partial replay re-running
    /// against a populated table doesn't fail.
    /// </summary>
    Task InsertBindingsAsync(
        string projectorName,
        Guid eventId,
        Guid tripId,
        DateTime occurredAt,
        IReadOnlyList<TripItemSnapshot> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Record the inbox row without writing any TripItems rows. Used
    /// when TripStartedIntegrationEvent.Items is empty — defers the
    /// binding write to a future enrichment event but prevents the same
    /// EventId from being re-processed.
    /// </summary>
    Task RecordEmptyBindingAsync(
        string projectorName,
        Guid eventId,
        Guid tripId,
        DateTime occurredAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh ItemStatus on every TripItems row of the given trip.
    /// Returns the row count updated. Used by Trip terminal events
    /// (Completed → "Delivered", Failed/Cancelled → "Unbound").
    /// </summary>
    Task<int> UpdateItemStatusForTripAsync(
        string projectorName,
        Guid eventId,
        Guid tripId,
        string newItemStatus,
        DateTime occurredAt,
        CancellationToken cancellationToken = default);
}
