using DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

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

// Phase P4: RequestedBy removed from the wire. UI callers cannot supply
// it — the handler stamps CurrentUser.Name into both CreatedBy and
// RequestedBy. Reintroduce only if an "on behalf of" flow is added,
// and even then read the actor from an ambient context, not a body field.
//
// Phase P5: ServiceWindow is required — symmetric with the system path
// (CreateUpstreamDeliveryOrderCommand). The domain has always modelled
// service windows as first-class scheduling input; the previous nullable
// shape only reflected a UI convenience that let users defer that field.
public record CreateDraftDeliveryOrderCommand(
    string OrderRef,
    ServiceWindowDto ServiceWindow,
    List<ItemDto> Items,
    Priority Priority = Priority.Normal,
    string? Notes = null,
    TransportMode? RequestedTransportMode = TransportMode.Amr,
    bool? RequiresDropPod = null,
    bool? RequiresPickupPod = null
) : ICommand<DeliveryOrderDetailDto>;
