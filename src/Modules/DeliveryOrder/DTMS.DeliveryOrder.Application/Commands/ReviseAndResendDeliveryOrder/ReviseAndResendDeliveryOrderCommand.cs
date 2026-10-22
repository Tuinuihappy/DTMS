using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Messaging;
using DeliveryOrderDetailDto = DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder.DeliveryOrderDetailDto;

namespace DTMS.DeliveryOrder.Application.Commands.ReviseAndResendDeliveryOrder;

/// <summary>
/// An <see cref="ItemDto"/> plus the Id of the order item it edits. A null
/// <paramref name="Id"/> adds a new item; order items no line names are removed.
/// </summary>
public record RevisedItemDto(
    Guid? Id,
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
    IReadOnlyList<HandlingInstruction>? HandlingInstructions = null)
    : ItemDto(ItemId, Description, PickupLocationCode, DropLocationCode, LoadUnitProfileCode,
        Dimensions, WeightKg, Quantity, Hazmat, Temperature, HandlingInstructions);

/// <summary>
/// "Edit &amp; resend" for a Failed order: overwrite priority, service window
/// and items (incl. pickup / drop codes), re-resolve the codes, and send the
/// order back through Planning as a fresh attempt — so the new trip's
/// lifecycle fires the shipment.* callbacks to the source system again.
/// OrderRef, transport mode and source system are not editable: they are
/// the source system's identity for the order.
/// </summary>
public record ReviseAndResendDeliveryOrderCommand(
    Guid OrderId,
    string Reason,
    Priority Priority,
    ServiceWindowDto ServiceWindow,
    List<RevisedItemDto> Items) : ICommand<DeliveryOrderDetailDto>;
