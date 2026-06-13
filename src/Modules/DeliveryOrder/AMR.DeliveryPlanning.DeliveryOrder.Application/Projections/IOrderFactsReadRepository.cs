namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P5 — Read side of bi.OrderFacts. Optimized for analyst-style
/// queries: window by date range, group by dimension, aggregate. The
/// read DTO mirrors the row 1:1 — projector / migration are the only
/// place schema lives.
/// </summary>
public interface IOrderFactsReadRepository
{
    Task<IReadOnlyList<OrderFactsEntry>> QueryAsync(
        OrderFactsFilters filters, CancellationToken ct);

    Task<int> CountAsync(OrderFactsFilters filters, CancellationToken ct);
}

/// <summary>
/// Window + dimension filters for the OrderFacts query. Pagination
/// happens at the caller level because reports cap at 50k rows.
/// </summary>
public record OrderFactsFilters(
    DateTime? FromCreatedAtUtc,
    DateTime? ToCreatedAtUtc,
    string? Priority,        // exact match
    string? FinalStatus,     // exact match
    string? SourceSystem,
    int Limit = 50_000);

/// <summary>
/// Wide-row DTO returned to the read endpoint + CSV exporter. Nullable
/// timestamps preserve the "not yet reached this status" semantics.
/// </summary>
public record OrderFactsEntry(
    Guid OrderId,
    string OrderRef,
    string SourceSystem,
    string Priority,
    string? TransportMode,
    string? RequestedBy,
    string FinalStatus,
    string? FailureReason,
    int TotalItems,
    double TotalQuantity,
    double TotalWeightKg,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? ConfirmedAt,
    DateTime? DispatchedAt,
    DateTime? InProgressAt,
    DateTime? CompletedAt,
    DateTime? PartiallyCompletedAt,
    DateTime? FailedAt,
    DateTime? CancelledAt,
    DateTime? RejectedAt,
    DateTime? HeldAt,
    DateTime? ReleasedAt,
    int? TimeToConfirmSec,
    int? TimeToDispatchSec,
    int? TimeToCompleteSec,
    bool? SlaConfirmBreached,
    bool? SlaCompleteBreached,
    DateTime UpdatedAt);
