using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using CommandItemDto = AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder.ItemDto;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.UpdateDraftDeliveryOrder;

public record UpdateDraftDeliveryOrderCommand(
    Guid OrderId,
    string OrderRef,
    Priority Priority,
    DateTime? RequestedDeliveryDate,
    List<CommandItemDto> Items,
    SlaTier SlaTier = SlaTier.Bronze
) : ICommand<DeliveryOrderDetailDto>;
