using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Commands.SubmitGeofenceOverride;

// POST /api/operator/geofence/override-request — operator failed a
// geofence check and is asking a supervisor for an exception. The
// supervisor approves/denies via a separate dispatcher-console
// endpoint (Phase 4.6) which calls GeofenceOverrideRequest.Approve/Deny.
public record SubmitGeofenceOverrideCommand(
    Guid TripId,
    Guid OperatorId,
    Guid ExpectedWarehouseId,
    double ReportedLat,
    double ReportedLng,
    string Reason,
    string? PhotoUrl) : ICommand<Guid>;
