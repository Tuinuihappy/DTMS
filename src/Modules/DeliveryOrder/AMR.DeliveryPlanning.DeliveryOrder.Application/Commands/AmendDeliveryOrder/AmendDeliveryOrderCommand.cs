using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;

public record AmendDeliveryOrderCommand(
    Guid OrderId,
    string Reason,
    DateTime? NewRequestedDeliveryDate,
    string? AmendedBy) : ICommand<Guid>;
