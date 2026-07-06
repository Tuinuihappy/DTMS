using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// POST /api/v1/source/trips/{tripId}/drop — a source system reports the
/// cargo has been dropped. Maps to <c>Trip.MarkVendorDropCompleted(...)</c>;
/// the parent order's RequiresDropPod policy is resolved here so downstream
/// projectors land items at Delivered vs DroppedOff correctly.
/// </summary>
public record SourceDropTripCommand(
    Guid TripId,
    string SourceSystemKey) : ICommand;
