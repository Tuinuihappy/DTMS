using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.RedispatchDeliveryOrder;

/// <summary>
/// Re-trigger the Planning dispatch loop for a Confirmed order whose
/// previous dispatch never produced a Trip (e.g., every group failed
/// vendor dispatch). Standard /trips/{id}/retry doesn't apply here —
/// there's no Trip ID to retry from.
///
/// Expected operator flow when an order is stuck Failed with no Trip:
///   1. POST /delivery-orders/{id}/reopen   → Order = Confirmed
///   2. (fix the underlying issue, e.g. register an OrderTemplate)
///   3. POST /delivery-orders/{id}/redispatch → Planning consumer
///      runs again, items rebind, fresh Trips dispatched.
/// </summary>
public record RedispatchDeliveryOrderCommand(
    Guid OrderId,
    string RedispatchedBy,
    string Reason,
    double WeightFallbackKg = 0) : ICommand;
