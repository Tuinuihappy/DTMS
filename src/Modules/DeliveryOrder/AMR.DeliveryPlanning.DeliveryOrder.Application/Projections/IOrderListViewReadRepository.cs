using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

public record OrderListViewFilters(
    OrderStatus? Status,
    StatusBucket? Bucket,
    Priority? Priority,
    TransportMode? TransportMode,
    string? Search,
    bool? HasFailedTrip,
    bool? HasActiveJob,
    string? SortBy,
    bool SortDescending);

public record OrderListViewEntry(
    Guid OrderId,
    string OrderRef,
    string SourceSystem,
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
}
