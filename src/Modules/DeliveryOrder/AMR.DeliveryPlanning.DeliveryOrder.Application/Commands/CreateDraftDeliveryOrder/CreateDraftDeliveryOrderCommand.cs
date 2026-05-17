using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public record DimensionsDto(double LengthMm, double WidthMm, double HeightMm);

public record QuantityDto(double Value, string Uom);

public record CargoSpecificDto(
    string? PartNo,
    string? Vendor,
    string? DateCode,
    string? TradingCode,
    string? InventoryNo,
    string? Po,
    string? TraceId,
    string? LotNo);

public record ItemDto(
    int ItemSeq,
    string Sku,
    string PickupLocationCode,
    string DropLocationCode,
    CargoType CargoType,
    DimensionsDto? Dimensions,
    double WeightKg,
    QuantityDto Quantity,
    CargoSpecificDto? CargoSpecific);

public record CreateDraftDeliveryOrderCommand(
    string OrderRef,
    Priority Priority,
    DateTime? RequestedDeliveryDate,
    List<ItemDto> Items,
    SourceSystem SourceSystem = SourceSystem.Manual
) : ICommand<DeliveryOrderDetailDto>;
