using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.CancelDeliveryOrder;

public record CancelDeliveryOrderCommand(Guid OrderId, string Reason) : ICommand;
