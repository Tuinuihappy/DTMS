using DTMS.SharedKernel.Messaging;

namespace DTMS.Transport.Manual.Application.Commands.RecordDrop;

// POST /api/operator/trips/{id}/drop — operator has arrived at the
// drop warehouse and is handing off the cargo. Same geofence-check
// semantics as pickup.
public record RecordDropCommand(
    Guid TripId,
    Guid OperatorId,
    double ReportedLat,
    double ReportedLng,
    string? PodKey) : ICommand;
