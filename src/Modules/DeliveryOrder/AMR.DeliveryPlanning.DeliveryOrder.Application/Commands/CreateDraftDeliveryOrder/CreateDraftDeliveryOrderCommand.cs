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

/// <summary>
/// Discriminated location reference. Provide exactly one of <c>Code</c> or <c>StationId</c>.
/// The chosen form is preserved end-to-end (response returns the same shape).
/// </summary>
public record LocationRefDto(string? Code, Guid? StationId);

public record ItemDto(
    string Sku,
    string? Description,
    LocationRefDto PickupLocation,
    LocationRefDto DropLocation,
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
