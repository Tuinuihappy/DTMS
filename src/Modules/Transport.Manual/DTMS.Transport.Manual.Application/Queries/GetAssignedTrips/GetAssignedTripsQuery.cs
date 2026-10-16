using DTMS.SharedKernel.Messaging;

namespace DTMS.Transport.Manual.Application.Queries.GetAssignedTrips;

// GET /api/operator/trips/assigned — returns the trip queue (active +
// recent) for the calling operator: the extension rows (what the operator
// did) plus what the pool card showed (order ref, pickup → drop, items),
// so a claimed trip still says where to go.
public record GetAssignedTripsQuery(Guid OperatorId) : IQuery<IReadOnlyList<AssignedTripDto>>;

public record AssignedTripDto(
    Guid TripId,
    // Dispatch Trip.Status name (InProgress, Completed, Cancelled, …);
    // null when the Trip row is missing.
    string? TripStatus,
    // Pool-card fields; null / 0 when the Trip row is missing.
    string? OrderRef,
    string? PickupCode,
    string? DropCode,
    int ItemCount,
    double TotalWeightKg,
    DateTime AssignedAt,
    DateTime? AcknowledgedAt,
    DateTime? PickedUpAt,
    DateTime? DroppedAt,
    DateTime? CompletedAt,
    DateTime? PickupDeadline,
    DateTime? DropDeadline,
    bool PickupOverrideUsed,
    bool DropOverrideUsed);
