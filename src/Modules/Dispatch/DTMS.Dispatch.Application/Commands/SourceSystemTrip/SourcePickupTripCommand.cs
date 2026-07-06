using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// POST /api/v1/source/trips/{tripId}/pickup — a source system reports the
/// cargo has been picked up. Maps to <c>Trip.MarkVendorPickedUp()</c>, the
/// same domain method the AMR pickup webhook fires. No geofence/POD — the
/// authenticated system is trusted (unlike the operator PWA path).
/// </summary>
public record SourcePickupTripCommand(
    Guid TripId,
    string SourceSystemKey) : ICommand;
