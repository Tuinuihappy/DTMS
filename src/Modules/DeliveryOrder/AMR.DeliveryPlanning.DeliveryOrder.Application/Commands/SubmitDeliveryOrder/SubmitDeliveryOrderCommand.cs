using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public record OrderLineDto(string ItemCode, double Quantity, double Weight, string? Remarks);
public record RecurringScheduleDto(string CronExpression, DateTime? ValidFrom, DateTime? ValidUntil);

public record SubmitDeliveryOrderCommand(
    string OrderKey,
    string PickupLocationCode,
    string DropLocationCode,
    OrderPriority Priority,
    DateTime? SLA,
    List<OrderLineDto> Lines,
    RecurringScheduleDto? Schedule
) : ICommand<Guid>;
