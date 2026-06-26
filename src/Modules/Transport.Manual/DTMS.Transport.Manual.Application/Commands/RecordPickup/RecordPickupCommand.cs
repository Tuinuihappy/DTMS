using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Commands.RecordPickup;

// POST /api/operator/trips/{id}/pickup — operator has arrived at the
// pickup warehouse and is collecting the cargo. Server-side geofence
// check enforces presence at the correct location (per ADR-016).
//
// PodKey: presigned MinIO object key the operator app uploaded the
// pickup photo to via the /api/operator/pod/presign endpoint (Phase 4.3
// will wire the actual presign). Null = no photo captured (allowed for
// pickup; drop will be stricter once Phase 4.5 frontend ships POD UX).
public record RecordPickupCommand(
    Guid TripId,
    Guid OperatorId,
    double ReportedLat,
    double ReportedLng,
    string? PodKey) : ICommand;
