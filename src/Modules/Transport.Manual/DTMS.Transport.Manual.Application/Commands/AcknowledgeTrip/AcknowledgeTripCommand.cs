using DTMS.SharedKernel.Messaging;

namespace DTMS.Transport.Manual.Application.Commands.AcknowledgeTrip;

// POST /api/operator/trips/{id}/acknowledge — operator confirms they've
// received the assignment and will start working on it. Sets
// ManualTripExtension.AcknowledgedAt; the watchdog uses this to clear
// the "no-ack" SLA alarm.
//
// WMS PR-4b — also serves the pool claim flow: when no extension exists
// for the trip the handler runs an atomic SQL CAS and either wins (start
// the trip) or returns AlreadyClaimedErrorCode so the endpoint replies 409.
public record AcknowledgeTripCommand(Guid TripId, Guid OperatorId) : ICommand;

public static class AcknowledgeTripErrorCodes
{
    // Sentinel the endpoint layer maps to HTTP 409 Conflict so the
    // operator PWA can toast "someone else took it" + refresh the pool.
    public const string AlreadyClaimed = "TRIP_ALREADY_CLAIMED";
}
