using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.HoldDeliveryOrder;

public record HoldDeliveryOrderCommand(Guid OrderId, string Reason, string? HeldBy = null) : ICommand;
