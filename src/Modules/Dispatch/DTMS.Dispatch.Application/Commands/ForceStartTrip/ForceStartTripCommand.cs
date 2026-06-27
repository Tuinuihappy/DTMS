using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.ForceStartTrip;

// Admin override for trips stuck at Created because RIOT3's TASK_PROCESSING
// webhook was dropped (or never fired). Runs the same Trip.MarkVendorStarted
// the vendor webhook would have — including the TripStartedIntegrationEvent
// cascade that triggers TripStartedOmsNotifyConsumer (→ POST /shipments).
public record ForceStartTripCommand(
    Guid TripId,
    string Reason,
    string? TriggeredBy,
    string? VendorVehicleKey,
    string? VendorVehicleName) : ICommand;
