using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// POST /api/v1/source/trips/{tripId}/pickup — a source system reports the
/// cargo has been picked up. Maps to <c>Trip.MarkVendorPickedUp(...)</c>, the
/// same domain method the AMR pickup webhook fires. No geofence/POD — the
/// authenticated system is trusted (unlike the operator PWA path).
/// <c>ActionBy</c>/<c>ActedAt</c> come from the body: WHO performed the pickup
/// in the caller's own system and WHEN — recorded on the ExecutionEvent audit
/// trail (there is no DTMS user on a system-to-system call).
/// </summary>
public record SourcePickupTripCommand(
    Guid TripId,
    string SourceSystemKey,
    string ActionBy,
    DateTime? ActedAt = null) : ICommand;
