using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// POST /api/v1/source/trips/{tripId}/complete — a source system reports the
/// trip is finished. Maps to <c>Trip.MarkVendorCompleted(...)</c> (→ Completed),
/// the same domain method the AMR TASK_FINISHED webhook fires.
/// <c>ActionBy</c>/<c>ActedAt</c> come from the body: WHO completed the trip in
/// the caller's own system and WHEN — recorded on the ExecutionEvent audit trail.
/// </summary>
public record SourceCompleteTripCommand(
    Guid TripId,
    string SourceSystemKey,
    string ActionBy,
    DateTime? ActedAt = null) : ICommand;
