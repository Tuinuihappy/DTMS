using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public record DimensionsDto(double LengthMm, double WidthMm, double HeightMm);

public record QuantityDto(double Value, string Uom);

public record ServiceWindowDto(DateTime? EarliestUtc, DateTime? LatestUtc);

public record HazmatDto(string ClassCode, PackingGroup? PackingGroup);

public record TemperatureRangeDto(double? MinC, double? MaxC);

public record ItemDto(
    string ItemId,
    string? Description,
    string PickupLocationCode,
    string DropLocationCode,
    string? LoadUnitProfileCode,
    DimensionsDto? Dimensions,
    double? WeightKg,
    QuantityDto Quantity,
    HazmatDto? Hazmat = null,
    TemperatureRangeDto? Temperature = null,
    IReadOnlyList<HandlingInstruction>? HandlingInstructions = null);

public record CreateDraftDeliveryOrderCommand(
    string OrderRef,
    ServiceWindowDto? ServiceWindow,
    List<ItemDto> Items,
    Priority Priority = Priority.Normal,
    string? RequestedBy = null,
    string? Notes = null,
    TransportMode? RequestedTransportMode = TransportMode.Amr,
    bool? RequiresDropPod = null,
    bool? RequiresPickupPod = null
) : ICommand<DeliveryOrderDetailDto>;
