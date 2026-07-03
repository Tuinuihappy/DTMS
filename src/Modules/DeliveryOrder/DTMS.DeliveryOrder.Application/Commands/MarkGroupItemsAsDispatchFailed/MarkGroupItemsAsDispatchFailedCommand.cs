using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.MarkGroupItemsAsDispatchFailed;

/// <summary>
/// Mark items of a (Pickup, Drop) group as Failed when the dispatcher
/// couldn't place a vendor order for that group. Used when multi-group
/// dispatch has partial success — failed-group items transition to Failed
/// (terminal) so the order can eventually reach PartiallyCompleted / Failed
/// instead of being stuck on pending items.
///
/// Accepts station Ids (AMR pairing) or WMS location Ids (Manual/Fleet
/// pairing). Caller (Planning consumer) supplies whichever the order's
/// mode uses; items match by either pair.
/// </summary>
public record MarkGroupItemsAsDispatchFailedCommand(
    Guid OrderId,
    Guid? PickupStationId,
    Guid? DropStationId,
    Guid? PickupWmsLocationId,
    Guid? DropWmsLocationId,
    string Reason
) : ICommand<int>;
