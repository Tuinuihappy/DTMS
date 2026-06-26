using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Commands.CompleteTrip;

// POST /api/operator/trips/{id}/complete — operator marks the trip
// done. Requires DroppedAt to be set on the extension (operator can't
// short-circuit the workflow). Trip aggregate transitions to Completed;
// Operator.ClearTripAssignment frees the operator for the next job.
public record CompleteTripCommand(Guid TripId, Guid OperatorId) : ICommand;
