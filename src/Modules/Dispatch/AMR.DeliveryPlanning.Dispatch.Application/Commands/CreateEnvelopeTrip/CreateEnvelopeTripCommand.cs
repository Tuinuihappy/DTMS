using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.CreateEnvelopeTrip;

/// <summary>
/// Create a Trip row for an envelope-dispatched order. No legs/tasks are
/// created — the vendor (RIOT3) owns execution and reports status via
/// webhook keyed by <paramref name="UpperKey"/>.
///
/// PickupStationId / DropStationId are persisted so a subsequent retry
/// can re-resolve the OrderTemplate without re-reading the delivery
/// order. AttemptNumber and PreviousAttemptId carry the retry lineage
/// (defaults match an unretried first dispatch).
/// </summary>
public record CreateEnvelopeTripCommand(
    Guid DeliveryOrderId,
    string UpperKey,
    string? VendorOrderKey,
    Guid? PickupStationId = null,
    Guid? DropStationId = null,
    int AttemptNumber = 1,
    Guid? PreviousAttemptId = null,
    string? TemplateNameAtDispatch = null,
    int? PriorityAtDispatch = null,
    string? VendorRequestSnapshot = null
) : ICommand<Guid>;
