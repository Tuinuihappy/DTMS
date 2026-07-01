using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;

namespace DTMS.DeliveryOrder.Application.Projections;

public record OrderListViewFilters(
    OrderStatus? Status,
    StatusBucket? Bucket,
    Priority? Priority,
    TransportMode? TransportMode,
    string? Search,
    bool? HasFailedTrip,
    bool? HasActiveJob,
    string? SortBy,
    bool SortDescending,
    // Inclusive [from, to] window applied to OrderListView.CreatedAt.
    // Either side may be omitted — open-ended range stays open on that
    // end. UTC instants (the projection stores CreatedAt as UTC).
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null);

public record OrderListViewEntry(
    Guid OrderId,
    string OrderRef,
    string SourceSystem,
    // Phase P5 — human-readable snapshot captured at create-time; null
    // for pre-P4 rows until the P5 normalize migration backfills them
    // from iam.SystemClients (and stays null forever for rows whose
    // origin SystemClient was removed before P5).
    string? SourceSystemDisplayName,
    string Priority,
    string Status,
    DateTime? SubmittedAt,
    string? CreatedBy,
    string? RequestedBy,
    string? Notes,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    double TotalWeightKg,
    double TotalQuantity,
    int TotalItems,
    string? TransportMode,
    bool? RequiresDropPod,
    bool? RequiresPickupPod,
    DateTime? ServiceWindowEarliestUtc,
    DateTime? ServiceWindowLatestUtc,
    bool HasFailedTrip,
    bool HasActiveJob,
    string? LatestJobStatus);

public interface IOrderListViewReadRepository
{
    /// <summary>
    /// Page through the projection with filter + sort + full-text search.
    /// Returns (items, total) for compatibility with the existing
    /// SearchAsync contract on the write-side repository.
    /// </summary>
    Task<(IReadOnlyList<OrderListViewEntry> Items, int TotalCount)> SearchAsync(
        OrderListViewFilters filters,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Group-by-status counts + total weight, sourced from the projection
    /// so the stats endpoint and the list table agree numerically.
    /// </summary>
    Task<DeliveryOrderStats> GetStatsAsync(CancellationToken cancellationToken = default);
}
