using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AssignItemsToTrip;

/// <summary>
/// Bind every item of a delivery order whose (PickupStationId,
/// DropStationId) pair matches the route to the given Trip. Issued by
/// the dispatcher immediately after a Trip is created (and again at
/// retry time so the new attempt becomes the authoritative owner).
/// Returns the number of items bound; 0 means no matching items —
/// which usually indicates an inconsistency the dispatcher should log.
/// </summary>
public record AssignItemsToTripCommand(
    Guid OrderId,
    Guid TripId,
    int AttemptNumber,
    Guid PickupStationId,
    Guid DropStationId
) : ICommand<int>;
