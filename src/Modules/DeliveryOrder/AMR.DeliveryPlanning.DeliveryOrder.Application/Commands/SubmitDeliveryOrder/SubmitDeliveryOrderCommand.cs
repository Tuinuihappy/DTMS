using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public record SubmitDeliveryOrderCommand(Guid OrderId) : ICommand<Guid>;
