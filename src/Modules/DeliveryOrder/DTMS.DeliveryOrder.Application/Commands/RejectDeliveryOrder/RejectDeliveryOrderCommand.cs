using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.RejectDeliveryOrder;

public record RejectDeliveryOrderCommand(Guid OrderId, string Reason, string? RejectedBy = null) : ICommand;
