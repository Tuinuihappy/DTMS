using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// POST /api/v1/source/trips/{tripId}/complete — a source system reports the
/// trip is finished. Maps to <c>Trip.MarkVendorCompleted()</c> (→ Completed),
/// the same domain method the AMR TASK_FINISHED webhook fires.
/// </summary>
public record SourceCompleteTripCommand(
    Guid TripId,
    string SourceSystemKey) : ICommand;
