using DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Messaging;
using CommandItemDto = DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder.ItemDto;
using ServiceWindowDto = DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder.ServiceWindowDto;

namespace DTMS.DeliveryOrder.Application.Commands.UpdateDraftDeliveryOrder;

public record UpdateDraftDeliveryOrderCommand(
    Guid OrderId,
    string OrderRef,
    Priority Priority,
    ServiceWindowDto? ServiceWindow,
    List<CommandItemDto> Items,
    string? RequestedBy = null,
    string? Notes = null,
    TransportMode? RequestedTransportMode = TransportMode.Amr
) : ICommand<DeliveryOrderDetailDto>;
