using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ConfirmDeliveryOrder;

public record ConfirmDeliveryOrderCommand(Guid OrderId, string? ConfirmedBy = null) : ICommand<Guid>;
