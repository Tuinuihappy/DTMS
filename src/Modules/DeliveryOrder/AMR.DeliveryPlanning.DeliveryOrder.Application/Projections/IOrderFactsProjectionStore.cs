namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P5 — Write side of the bi.OrderFacts projection. One mutator
/// method per lifecycle event. <c>UpsertOnConfirmAsync</c> is the only
/// path that may create the row (Confirmed is the first event that
/// carries enough dimensional data); every other mutator no-ops if
/// the row is missing — backfill SQL is responsible for seeding rows
/// for orders that confirmed before P5 shipped.
/// </summary>
public interface IOrderFactsProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken ct);
    Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken ct);

    Task UpsertOnConfirmAsync(
        Guid orderId,
        DateTime confirmedAt,
        string priority,
        string? transportMode,
        int totalItems,
        double totalWeightKg,
        CancellationToken ct);

    Task SetSubmittedAtAsync(Guid orderId, DateTime at, CancellationToken ct);
    Task SetDispatchedAtAsync(Guid orderId, DateTime at, CancellationToken ct);
    Task SetInProgressAtAsync(Guid orderId, DateTime at, CancellationToken ct);
    Task SetCompletedAtAsync(Guid orderId, DateTime at, CancellationToken ct);
    Task SetPartiallyCompletedAtAsync(Guid orderId, DateTime at, CancellationToken ct);
    Task SetFailedAtAsync(Guid orderId, DateTime at, string? reason, CancellationToken ct);
    Task SetCancelledAtAsync(Guid orderId, DateTime at, string? reason, CancellationToken ct);
    Task SetRejectedAtAsync(Guid orderId, DateTime at, string? reason, CancellationToken ct);
    Task SetHeldAtAsync(Guid orderId, DateTime at, string? reason, CancellationToken ct);
    Task SetReleasedAtAsync(Guid orderId, DateTime at, CancellationToken ct);
}
