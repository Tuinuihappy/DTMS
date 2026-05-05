using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public record DimsDto(double LengthMm, double WidthMm, double HeightMm);
public record TemperatureRangeDto(double? MinCelsius, double? MaxCelsius);

public record OrderItemDto(
    string PickupLocationCode,
    string DropLocationCode,
    string? WorkOrder,
    string ItemNumber,
    string ItemDescription,
    double Quantity,
    double? Weight,
    LoadUnitType LoadUnitType,
    string? Line = null,
    string? Model = null,
    string? Remarks = null,
    DimsDto? Dims = null,
    int? HazmatClass = null,
    TemperatureRangeDto? TemperatureRange = null,
    List<HandlingInstruction>? HandlingInstructions = null);

public record ServiceWindowDto(DateTime? Earliest, DateTime? Latest);

public record RecurringScheduleDto(string CronExpression, DateTime? ValidFrom, DateTime? ValidUntil);

public record CreateDraftDeliveryOrderCommand(
    string OrderName,
    SlaTier SlaTier,
    ServiceWindowDto? ServiceWindow,
    StructureType StructureType,
    List<string>? Tags,
    List<OrderItemDto> OrderItems,
    RecurringScheduleDto? Schedule
) : ICommand<Guid>;
