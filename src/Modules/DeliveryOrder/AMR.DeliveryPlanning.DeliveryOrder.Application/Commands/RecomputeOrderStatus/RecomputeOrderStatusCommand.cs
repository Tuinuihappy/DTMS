using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.RecomputeOrderStatus;

/// <summary>
/// Forces a refresh of the Order's derived status from its items
/// (Gap 5 — item-level state derivation). Fired by the Planning
/// consumer's all-groups-failed branch so an order with every item
/// already marked Failed doesn't sit at "Planned" forever waiting
/// for a TripConsumer that will never run (no Trip was created).
///
/// Idempotent — RecomputeStatusFromItems is a no-op when the items
/// are still in-flight, when the order is already terminal, or when
/// an admin override (Cancelled / Rejected / Held) is set.
/// </summary>
public record RecomputeOrderStatusCommand(Guid OrderId) : ICommand;
