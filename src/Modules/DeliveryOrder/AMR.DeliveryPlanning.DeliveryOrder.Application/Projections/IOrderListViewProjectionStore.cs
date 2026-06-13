namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P4 — Mutation abstraction for the OrderListView projection.
/// The projector calls one method per intent; the implementation reads
/// the row, applies the mutation, and saves. <see cref="UpsertOnConfirmAsync"/>
/// is the only method that creates a row — every other handler updates
/// the existing row (no-op if the row doesn't exist yet, since the
/// projector subscribes to events that can fire pre-Confirm).
/// </summary>
public interface IOrderListViewProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create or replace the row from a DeliveryOrderConfirmedIntegrationEventV1
    /// payload. Confirmed is the canonical "row exists" event — every
    /// other handler is a status-only update against an existing row.
    /// </summary>
    Task UpsertOnConfirmAsync(
        Guid orderId,
        string orderRef,
        string status,
        string sourceSystem,
        string priority,
        string? transportMode,
        string? requestedBy,
        string? createdBy,
        string? notes,
        int totalItems,
        double totalQuantity,
        double totalWeightKg,
        bool? requiresDropPod,
        bool? requiresPickupPod,
        DateTime createdAt,
        DateTime? submittedAt,
        DateTime? serviceWindowEarliestUtc,
        DateTime? serviceWindowLatestUtc,
        string searchText,
        CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(Guid orderId, string newStatus, DateTime occurredAt, CancellationToken cancellationToken = default);

    Task SetTripDerivedFieldsAsync(Guid orderId, bool hasFailedTrip, Guid? latestTripId, CancellationToken cancellationToken = default);

    Task SetJobDerivedFieldsAsync(Guid orderId, bool hasActiveJob, string? latestJobStatus, CancellationToken cancellationToken = default);
}
