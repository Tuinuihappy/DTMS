using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkGroupItemsAsDispatchFailed;

/// <summary>
/// Mark items of a (Pickup, Drop) station-pair group as Failed when the
/// dispatcher couldn't place a vendor order for that group. Used when
/// multi-group dispatch has partial success — failed-group items
/// transition to Failed (terminal) so the order can eventually reach
/// PartiallyCompleted instead of being stuck on pending items.
/// </summary>
public record MarkGroupItemsAsDispatchFailedCommand(
    Guid OrderId,
    Guid PickupStationId,
    Guid DropStationId,
    string Reason
) : ICommand<int>;
