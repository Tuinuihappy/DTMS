using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CancelDeliveryOrder;

public record CancelDeliveryOrderCommand(Guid OrderId, string Reason) : ICommand;
