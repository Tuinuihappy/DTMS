using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;

public record AmendDeliveryOrderCommand(
    Guid OrderId,
    string Reason,
    ServiceWindowDto? NewServiceWindow,
    string? AmendedBy) : ICommand<Guid>;
