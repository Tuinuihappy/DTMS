using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.AmendDeliveryOrder;

public record AmendDeliveryOrderCommand(
    Guid OrderId,
    string Reason,
    ServiceWindowDto? NewServiceWindow,
    string? AmendedBy) : ICommand<Guid>;
