using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Domain.Enums;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Queries.GetTripsQueue;

/// <summary>
/// Paginated operator Trips list. Mirrors the GetJobsQueue shape so the
/// frontend can drive Status / Vehicle / Date / search filters from one
/// endpoint. Empty Statuses = no status filter (returns all). Items
/// default to newest-first (CreatedAt desc).
/// </summary>
public record GetTripsQueueQuery(
    IReadOnlyList<TripStatus> Statuses,
    string? Search,
    string? VehicleKey,
    DateTime? FromUtc,
    DateTime? ToUtc,
    TripQueueSort SortBy,
    bool SortDescending,
    int Page,
    int PageSize
) : IQuery<TripsQueueResult>;

public sealed record TripsQueueResult(
    IReadOnlyList<TripQueueItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record TripQueueItemDto(
    Guid Id,
    Guid DeliveryOrderId,
    string? OrderRef,
    Guid JobId,
    Guid? VehicleId,
    string? VendorVehicleKey,
    string? VendorVehicleName,
    string Status,
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
