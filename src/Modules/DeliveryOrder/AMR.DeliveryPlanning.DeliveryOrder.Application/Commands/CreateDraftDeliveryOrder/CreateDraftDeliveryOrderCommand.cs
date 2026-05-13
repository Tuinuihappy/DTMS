using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public record DimensionsDto(double LengthCm, double WidthCm, double HeightCm);

public record QuantityDto(double Value, string Uom);

public record CargoSpecificDto(
    string? PartNo,
    string? Vendor,
    string? DateCode,
    string? TradingCode,
    string? InventoryNo,
    string? Po,
    string? TraceId);

public record ItemDto(
    string Sku,
    string PickupLocationCode,
    string DropLocationCode,
    DimensionsDto? Dimensions,
    double WeightKg,
    QuantityDto Quantity,
    CargoSpecificDto? CargoSpecific);

public record CreateDraftDeliveryOrderCommand(
    string OrderRef,
    Priority Priority,
    CargoType CargoType,
    DateTime? RequestedTime,
    List<ItemDto> Items
) : ICommand<Guid>;
