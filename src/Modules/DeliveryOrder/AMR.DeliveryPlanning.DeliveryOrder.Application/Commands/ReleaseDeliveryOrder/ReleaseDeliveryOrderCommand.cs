using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ConfirmDeliveryOrder;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ReleaseDeliveryOrder;

// Release re-enters Confirmed and re-fires DeliveryOrderConfirmedIntegrationEvent, so the
// response shape mirrors Confirm: caller may want to surface the same quality warnings
// (e.g. missing weights) that they would have seen on the original confirmation.
public record ReleaseDeliveryOrderCommand(Guid OrderId, string? ReleasedBy = null)
    : ICommand<ConfirmDeliveryOrderResult>;
