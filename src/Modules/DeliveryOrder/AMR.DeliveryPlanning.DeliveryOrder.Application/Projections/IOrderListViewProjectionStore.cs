namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P4 — Mutation abstraction for the OrderListView projection.
/// The projector calls one method per intent; the implementation reads
/// the row, applies the mutation, and saves. <see cref="UpsertOnCreateAsync"/>
/// is the only method that creates a row — every other handler updates
/// the existing row (no-op if the row doesn't exist yet).
///
/// Phase P4.5 (2026-06-15) — row is now created from the new
/// DeliveryOrderCreatedIntegrationEventV1 (fired once per order after items
/// are populated), not from Confirmed. Confirmed/Submitted/Validated are
/// status-only updates.
/// </summary>
public interface IOrderListViewProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent row materialization from a DeliveryOrderCreatedIntegrationEventV1
    /// payload. Created is the canonical "row exists" event — every other
    /// handler is a status-only update against an existing row.
    /// </summary>
    Task UpsertOnCreateAsync(
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
