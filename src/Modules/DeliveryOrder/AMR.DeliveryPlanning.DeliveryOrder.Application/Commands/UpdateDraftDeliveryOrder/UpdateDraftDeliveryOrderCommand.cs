using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using CommandItemDto = AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder.ItemDto;
using ServiceWindowDto = AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder.ServiceWindowDto;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.UpdateDraftDeliveryOrder;

public record UpdateDraftDeliveryOrderCommand(
    Guid OrderId,
    string OrderRef,
    Priority Priority,
    ServiceWindowDto? ServiceWindow,
    List<CommandItemDto> Items,
    string? RequestedBy = null,
    string? Notes = null
) : ICommand<DeliveryOrderDetailDto>;
