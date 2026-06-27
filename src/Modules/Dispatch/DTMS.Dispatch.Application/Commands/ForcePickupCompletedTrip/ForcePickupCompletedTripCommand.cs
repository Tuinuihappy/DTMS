using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.ForcePickupCompletedTrip;

// Admin override for trips missing the pickup sub-task webhook. Runs the
// same Trip.MarkVendorPickedUp the vendor webhook would have. Does NOT
// notify the upstream OMS — pickup is an internal item-state transition
// (Pending → Picked); only Start (/shipments) and Drop (/arrived) reach
// the upstream system.
public record ForcePickupCompletedTripCommand(
    Guid TripId,
    string Reason,
    string? TriggeredBy) : ICommand;
