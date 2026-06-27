using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.CapturePoD;

public record CapturePodCommand(
    Guid TripId,
    Guid StopId,
    string? PhotoUrl,
    string? SignatureData,
    List<string>? ScannedIds,
    string? Notes) : ICommand<Guid>;
