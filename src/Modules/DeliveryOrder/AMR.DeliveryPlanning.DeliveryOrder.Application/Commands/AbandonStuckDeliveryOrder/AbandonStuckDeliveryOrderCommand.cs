using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AbandonStuckDeliveryOrder;

/// <summary>
/// Operator escape hatch (Phase b11 / Option B) for orders left at
/// Dispatched after every Trip went terminal-Cancelled — typically
/// legacy rows from before the cascade fix landed in TripCancelledConsumer.
/// Validates the order is in-flight AND has zero active trips, then
/// calls Order.Cancel so items and the order both reach a terminal state.
/// </summary>
public record AbandonStuckDeliveryOrderCommand(
    Guid OrderId,
    string AbandonedBy,
    string Reason
) : ICommand;
