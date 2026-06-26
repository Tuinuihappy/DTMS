using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetOrderStatusHistory;

public record GetOrderStatusHistoryQuery(Guid OrderId) : IQuery<OrderStatusHistoryResponse>;

public record OrderStatusHistoryEntryDto(
    Guid EventId,
    string? FromStatus,
    string ToStatus,
    DateTime OccurredAt,
    string? Reason);

public record OrderStatusHistoryResponse(
    Guid OrderId,
    IReadOnlyList<OrderStatusHistoryEntryDto> Entries,
    /// <summary>
    /// Most recent OccurredAt across the returned entries — null when the
    /// list is empty. Frontend uses this to drive
    /// <c>&lt;DataFreshnessChip /&gt;</c> without a separate metadata fetch.
    /// </summary>
    DateTime? LastEventAt);
