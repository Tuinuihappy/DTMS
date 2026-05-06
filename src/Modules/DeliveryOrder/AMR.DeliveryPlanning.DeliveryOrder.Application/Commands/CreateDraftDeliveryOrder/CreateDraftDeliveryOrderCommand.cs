using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public record PackageContentDto(string ItemNumber, double Quantity);

public record PackageDto(
    string PickupLocationCode,
    string DropLocationCode,
    string Barcode,
    string LoadUnitProfileCode,
    double GrossWeightKg,
    List<PackageContentDto>? Contents = null);

public record ServiceWindowDto(DateTime? Earliest, DateTime? Latest);

public record RecurringScheduleDto(string CronExpression, DateTime? ValidFrom, DateTime? ValidUntil);

public record CreateDraftDeliveryOrderCommand(
    string OrderName,
    SlaTier SlaTier,
    ServiceWindowDto? ServiceWindow,
    StructureType StructureType,
    List<string>? Tags,
    List<PackageDto> OrderItems,
    RecurringScheduleDto? Schedule
) : ICommand<Guid>;
