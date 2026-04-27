using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;

public record AmendDeliveryOrderCommand(
    Guid OrderId,
    string Reason,
    OrderPriority? NewPriority,
    DateTime? NewSla,
    string? AmendedBy) : ICommand<Guid>;
