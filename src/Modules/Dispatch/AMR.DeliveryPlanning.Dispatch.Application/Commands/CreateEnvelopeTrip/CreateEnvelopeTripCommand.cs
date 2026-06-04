using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.CreateEnvelopeTrip;

/// <summary>
/// Create a Trip row for an envelope-dispatched order. No legs/tasks are
/// created — the vendor (RIOT3) owns execution and reports status via
/// webhook keyed by <paramref name="UpperKey"/>.
/// </summary>
public record CreateEnvelopeTripCommand(
    Guid DeliveryOrderId,
    string UpperKey,
    string? VendorOrderKey
) : ICommand<Guid>;
