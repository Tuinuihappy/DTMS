using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public record OrderLineDto(
    string PickupLocationCode,
    string DropLocationCode,
    int WorkOrderId,
    string WorkOrder,
    int ItemId,
    string ItemNumber,
    string ItemDescription,
    double Quantity,
    double Weight,
    string? Remarks);

public record RecurringScheduleDto(string CronExpression, DateTime? ValidFrom, DateTime? ValidUntil);

public record SubmitDeliveryOrderCommand(
    string OrderKey,
    OrderPriority Priority,
    DateTime? SLA,
    List<OrderLineDto> Lines,
    RecurringScheduleDto? Schedule
) : ICommand<Guid>;
