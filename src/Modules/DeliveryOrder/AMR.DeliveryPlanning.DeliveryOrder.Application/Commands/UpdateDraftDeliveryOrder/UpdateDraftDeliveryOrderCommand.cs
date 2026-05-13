using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.UpdateDraftDeliveryOrder;

public record UpdateDraftDeliveryOrderCommand(
    Guid OrderId,
    string OrderRef,
    Priority Priority,
    CargoType CargoType,
    DateTime? RequestedTime,
    List<ItemDto> Items
) : ICommand<Guid>;
