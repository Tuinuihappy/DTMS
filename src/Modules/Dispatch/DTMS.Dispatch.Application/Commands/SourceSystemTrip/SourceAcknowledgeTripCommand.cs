using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// POST /api/v1/source/trips/{tripId}/acknowledge — a source system reports
/// it has accepted and started this trip. Maps to
/// <c>Trip.MarkVendorStarted()</c> (Created → InProgress), mirroring the AMR
/// RIOT3 <c>TASK_PROCESSING</c> path. <c>SourceSystemKey</c> is pinned from
/// the authenticated principal so the payload can't spoof its origin.
/// </summary>
public record SourceAcknowledgeTripCommand(
    Guid TripId,
    string SourceSystemKey) : ICommand;
