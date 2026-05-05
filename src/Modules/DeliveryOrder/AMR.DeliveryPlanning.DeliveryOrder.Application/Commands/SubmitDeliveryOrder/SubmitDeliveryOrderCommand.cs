using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public record OrderItemDto(
    string PickupLocationCode,
    string DropLocationCode,
    int WorkOrderId,
    string WorkOrder,
    int ItemId,
    string ItemNumber,
    string ItemDescription,
    double Quantity,
    double Weight,
    string? Line,
    string? Model,
    string? Remarks);

public record RecurringScheduleDto(string CronExpression, DateTime? ValidFrom, DateTime? ValidUntil);

public record SubmitDeliveryOrderCommand(
    int OrderId,
    string OrderNo,
    string CreateBy,
    OrderPriority Priority,
    DateTime? SLA,
    List<OrderItemDto> OrderItems,
    RecurringScheduleDto? Schedule
) : ICommand<Guid>;
