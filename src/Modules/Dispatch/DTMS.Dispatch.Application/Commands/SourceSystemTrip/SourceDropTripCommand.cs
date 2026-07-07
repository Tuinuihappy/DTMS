using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// POST /api/v1/source/trips/{tripId}/drop — a source system reports the
/// cargo has been dropped. Maps to <c>Trip.MarkVendorDropCompleted(...)</c>;
/// the parent order's RequiresDropPod policy is resolved here so downstream
/// projectors land items at Delivered vs DroppedOff correctly.
/// <c>ActionBy</c>/<c>ActedAt</c> come from the body: WHO performed the drop in
/// the caller's own system and WHEN — recorded on the ExecutionEvent audit trail.
/// </summary>
public record SourceDropTripCommand(
    Guid TripId,
    string SourceSystemKey,
    string ActionBy,
    DateTime? ActedAt = null) : ICommand;
