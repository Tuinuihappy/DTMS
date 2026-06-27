using DTMS.Dispatch.Domain.Enums;

namespace DTMS.Dispatch.Application.Projections;

/// <summary>
/// Read-side abstraction for the operator Trips list. Composes a
/// paginated query over dispatch.Trips with optional filters and
/// left-joins dispatch.TripItems so the list can show a human-readable
/// OrderRef without an N+1 round trip per row.
/// </summary>
public interface ITripQueueReadRepository
{
    Task<TripQueuePage> SearchAsync(TripQueueFilter filter, CancellationToken cancellationToken = default);
}

public sealed record TripQueueFilter(
    IReadOnlyList<TripStatus> Statuses,
    string? Search,
    string? VehicleKey,
    DateTime? FromUtc,
    DateTime? ToUtc,
    TripQueueSort SortBy,
    bool SortDescending,
    int Page,
    int PageSize);

public enum TripQueueSort
{
    CreatedAt,
    StartedAt,
    CompletedAt,
    AttemptNumber,
    Status,
    Priority,
}

public sealed record TripQueuePage(
    IReadOnlyList<TripQueueItem> Items,
    int TotalCount);

public sealed record TripQueueItem(
    Guid Id,
    Guid DeliveryOrderId,
    string? OrderRef,
    Guid JobId,
    Guid? VehicleId,
    string? VendorVehicleKey,
    string? VendorVehicleName,
    TripStatus Status,
    int AttemptNumber,
    Guid? PreviousAttemptId,
    string UpperKey,
    string? VendorOrderKey,
    string? TemplateNameAtDispatch,
    int? PriorityAtDispatch,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? VendorExpectedCompletionAt,
    string? FailureReason,
    Guid? PickupStationId,
    Guid? DropStationId);
