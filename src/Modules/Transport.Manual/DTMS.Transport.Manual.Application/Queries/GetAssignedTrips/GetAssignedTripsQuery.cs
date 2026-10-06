using DTMS.SharedKernel.Messaging;

namespace DTMS.Transport.Manual.Application.Queries.GetAssignedTrips;

// GET /api/operator/trips/assigned — returns the trip queue (active +
// recent) for the calling operator. Phase 4.2 returns the extension
// rows only; Phase 4.4 will join Trip + DeliveryOrder for richer DTOs
// (cargo description, pickup address, ETA).
public record GetAssignedTripsQuery(Guid OperatorId) : IQuery<IReadOnlyList<AssignedTripDto>>;

public record AssignedTripDto(
    Guid TripId,
    // Dispatch Trip.Status name (InProgress, Completed, Cancelled, …);
    // null when the Trip row is missing.
    string? TripStatus,
    DateTime AssignedAt,
    DateTime? AcknowledgedAt,
    DateTime? PickedUpAt,
    DateTime? DroppedAt,
    DateTime? CompletedAt,
    DateTime? PickupDeadline,
    DateTime? DropDeadline,
    bool PickupOverrideUsed,
    bool DropOverrideUsed);
