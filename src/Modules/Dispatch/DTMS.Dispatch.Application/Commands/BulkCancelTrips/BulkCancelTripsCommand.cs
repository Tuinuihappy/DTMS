using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.BulkCancelTrips;

// Backend Phase 2 — dispatcher-facing bulk cancel for the trips table.
// Mirrors BulkCancelDeliveryOrders shape so the frontend can reuse the
// same response handling logic (succeeded[] + failures[], 207 on
// partial). Each id is dispatched through the existing
// CancelTripCommand to preserve per-row domain invariants (RIOT3
// vendor cancel, status guard rails, etc.).
public record BulkCancelTripsCommand(List<Guid> TripIds, string Reason)
    : ICommand<BulkCancelTripsResult>;

public record BulkCancelTripsResult(
    List<Guid> Succeeded,
    List<BulkCancelTripFailure> Failures);

public record BulkCancelTripFailure(Guid TripId, string Reason);
