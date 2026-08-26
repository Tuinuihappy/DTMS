using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.MarkAllItemsAsDispatchFailed;

/// <summary>
/// Mark every unbound Pending item of the order as Failed. For failures
/// that precede grouping entirely — the order's transport mode has no
/// registered dispatch strategy, so no (Pickup, Drop) group ever exists
/// to key <see cref="MarkGroupItemsAsDispatchFailed.MarkGroupItemsAsDispatchFailedCommand"/>
/// on. Caller pairs this with RecomputeOrderStatusCommand so the order
/// lands at Failed instead of stalling on in-flight items.
/// </summary>
public record MarkAllItemsAsDispatchFailedCommand(
    Guid OrderId,
    string Reason
) : ICommand<int>;
