using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.MarkJobDispatched;

/// <summary>
/// Phase b8 — Called from DeliveryOrderValidatedConsumer after a successful
/// envelope dispatch. Links the Job anchor to the Trip row that resulted
/// and the vendor's correlation key.
/// </summary>
public record MarkJobDispatchedCommand(
    Guid JobId,
    Guid TripId,
    string? VendorOrderKey
) : ICommand;
