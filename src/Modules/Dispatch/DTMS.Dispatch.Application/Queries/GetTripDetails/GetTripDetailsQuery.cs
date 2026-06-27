using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Queries.GetTripDetails;

/// <summary>
/// Returns the full operator-facing view of a Trip: aggregate state,
/// snapshot fields, per-mission timeline, and (optionally) the raw
/// vendor JSON blobs for compliance.
/// </summary>
public record GetTripDetailsQuery(
    Guid TripId,
    bool IncludeRawSnapshots = false
) : IQuery<TripDetailsDto>;

public sealed record TripDetailsDto(
    Guid Id,
    Guid DeliveryOrderId,
    string Status,
    int AttemptNumber,
    Guid? PreviousAttemptId,
    string UpperKey,
    string? VendorOrderKey,
    string? VendorVehicleKey,
    string? VendorVehicleName,
    string? TemplateNameAtDispatch,
    int? PriorityAtDispatch,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? VendorExpectedCompletionAt,
    string? FailureReason,
    Guid? PickupStationId,
    Guid? DropStationId,
    IReadOnlyList<TripMissionDto> Missions,
    string? VendorRequestSnapshot,    // populated only when IncludeRawSnapshots = true
    string? VendorFinalSnapshot);

public sealed record TripMissionDto(
    int MissionIndex,
    string MissionKey,
    string MissionType,
    string State,
    string? StationName,
    string? ActionName,
    string? ActionType,
    string? ResultCode,
    string? ErrorMessage,
    DateTime ChangeStateTime,
    DateTime ReceivedAt);
