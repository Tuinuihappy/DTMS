using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public record DimensionsDto(double LengthMm, double WidthMm, double HeightMm);

public record QuantityDto(double Value, string Uom);

public record CargoSpecificDto(
    string? PartNo,
    string? Wo,
    string? Line,
    string? Vendor,
    string? DateCode,
    string? TradingCode,
    string? InventoryNo,
    string? Po,
    string? TraceId,
    string? LotNo);

public record ItemDto(
    string Sku,
    string? Description,
    string PickupLocationCode,
    string DropLocationCode,
    string? LoadUnitProfileCode,
    DimensionsDto? Dimensions,
    double? WeightKg,
    QuantityDto Quantity,
    CargoType? CargoType,
    CargoSpecificDto? CargoSpecific);

public record CreateDraftDeliveryOrderCommand(
    string OrderRef,
    DateTime? RequestedDeliveryDate,
    List<ItemDto> Items,
    Priority Priority = Priority.Normal,
    SourceSystem SourceSystem = SourceSystem.Manual,
    string? CreatedBy = null
) : ICommand<DeliveryOrderDetailDto>;
