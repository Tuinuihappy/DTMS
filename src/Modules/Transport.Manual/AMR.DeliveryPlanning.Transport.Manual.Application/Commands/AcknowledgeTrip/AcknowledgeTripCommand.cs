using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Commands.AcknowledgeTrip;

// POST /api/operator/trips/{id}/acknowledge — operator confirms they've
// received the assignment and will start working on it. Sets
// ManualTripExtension.AcknowledgedAt; the watchdog uses this to clear
// the "no-ack" SLA alarm.
public record AcknowledgeTripCommand(Guid TripId, Guid OperatorId) : ICommand;
