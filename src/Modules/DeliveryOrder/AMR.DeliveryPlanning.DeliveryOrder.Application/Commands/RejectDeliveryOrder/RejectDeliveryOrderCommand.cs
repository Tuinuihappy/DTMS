using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.RejectDeliveryOrder;

public record RejectDeliveryOrderCommand(Guid OrderId, string Reason, string? RejectedBy = null) : ICommand;
