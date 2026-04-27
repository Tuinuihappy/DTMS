using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.CapturePoD;

public record CapturePodCommand(
    Guid TripId,
    Guid StopId,
    string? PhotoUrl,
    string? SignatureData,
    List<string>? ScannedIds,
    string? Notes) : ICommand<Guid>;
