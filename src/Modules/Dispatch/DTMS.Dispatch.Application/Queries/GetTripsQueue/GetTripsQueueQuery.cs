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
    // Manual pool trips: the operator who claimed the trip (null for AMR /
    // unclaimed trips — those use the vendorVehicle* fields instead).
    Guid? ClaimedByOperatorId,
    string? ClaimedByOperatorName,
    // Order requester (DeliveryOrder.RequestedBy). Last-resort label for the
    // Vehicle/Operator column when a manual / self-managed trip carries no
    // vendor vehicle and was never claimed by a pool operator.
    string? RequestedBy,
    // Order transport mode ("Amr" | "Manual" | "Fleet"). Lets the UI interpret
    // the executor label per mode — AMR trips must NOT fall back to RequestedBy.
    string? TransportMode,
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
