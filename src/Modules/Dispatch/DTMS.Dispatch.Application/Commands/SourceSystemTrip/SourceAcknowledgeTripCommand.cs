using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// POST /api/v1/source/trips/{tripId}/acknowledge — a source system reports
/// it has accepted and started this trip. Maps to
/// <c>Trip.AcknowledgeBySource(...)</c> (Created → InProgress), mirroring the
/// AMR RIOT3 <c>TASK_PROCESSING</c> path but additionally recording WHO
/// acknowledged. <c>SourceSystemKey</c> is pinned from the authenticated
/// principal so the payload can't spoof its origin; <c>AcknowledgedBy</c> and
/// <c>AcknowledgedAt</c> come from the request body (the caller's own user —
/// there is no DTMS user on a system-to-system call).
/// </summary>
public record SourceAcknowledgeTripCommand(
    Guid TripId,
    string SourceSystemKey,
    string AcknowledgedBy,
    DateTime? AcknowledgedAt = null) : ICommand;
