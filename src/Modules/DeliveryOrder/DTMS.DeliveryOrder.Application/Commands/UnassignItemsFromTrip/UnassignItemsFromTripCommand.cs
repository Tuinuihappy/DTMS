using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.UnassignItemsFromTrip;

/// <summary>
/// Release a Trip's items so the next retry can rebind them. Fired when
/// a Trip is Cancelled and the order is still live — leaves item status
/// in place (a Picked item stays Picked) but clears TripId so the items
/// are discoverable as "awaiting next dispatch".
/// </summary>
public record UnassignItemsFromTripCommand(
    Guid OrderId,
    Guid TripId
) : ICommand<int>;
