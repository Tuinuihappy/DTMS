using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.ForceDropCompletedTrip;

// Admin override for trips missing the drop sub-task webhook. Runs the
// same Trip.MarkVendorDropCompleted the vendor webhook would have —
// including the TripDropCompletedIntegrationEvent cascade that triggers
// TripDropCompletedOmsNotifyConsumer (→ POST /arrived). The order's
// RequiresDropPod policy is resolved server-side from the parent order
// so callers don't have to know it.
public record ForceDropCompletedTripCommand(
    Guid TripId,
    string Reason,
    string? TriggeredBy) : ICommand;
