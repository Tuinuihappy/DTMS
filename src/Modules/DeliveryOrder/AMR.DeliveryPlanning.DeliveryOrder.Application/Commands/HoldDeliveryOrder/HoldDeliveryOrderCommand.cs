using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.HoldDeliveryOrder;

public record HoldDeliveryOrderCommand(Guid OrderId, string Reason, string? HeldBy = null) : ICommand;
