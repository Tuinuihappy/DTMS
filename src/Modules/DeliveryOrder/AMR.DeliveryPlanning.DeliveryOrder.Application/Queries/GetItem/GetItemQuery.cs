using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetItem;

public record ItemDetailDto(
    Guid Id,
    Guid DeliveryOrderId,
    int ItemSeq,
    string ItemId,
    string? Description,
    string PickupLocationCode,
    string DropLocationCode,
    Guid? PickupStationId,
    Guid? DropStationId,
    string? LoadUnitProfileCode,
    DimensionsDto? Dimensions,
    double? WeightKg,
    QuantityDto Quantity,
    HazmatDto? Hazmat,
    TemperatureRangeDto? Temperature,
    IReadOnlyList<HandlingInstruction> HandlingInstructions,
    ItemStatus Status);

public record GetItemQuery(Guid ItemId) : IQuery<ItemDetailDto>;

public class GetItemQueryHandler : IQueryHandler<GetItemQuery, ItemDetailDto>
{
    private readonly IDeliveryOrderRepository _repo;
    public GetItemQueryHandler(IDeliveryOrderRepository repo) => _repo = repo;

    public async Task<Result<ItemDetailDto>> Handle(GetItemQuery request, CancellationToken cancellationToken)
    {
        var item = await _repo.GetItemByIdAsync(request.ItemId, cancellationToken);
        if (item is null)
            return Result<ItemDetailDto>.Failure($"Item {request.ItemId} not found.");

        var dto = new ItemDetailDto(
            item.Id,
            item.DeliveryOrderId,
            item.ItemSeq,
            item.ItemId,
            item.Description,
            item.PickupLocationCode,
            item.DropLocationCode,
            item.PickupStationId,
            item.DropStationId,
            item.LoadUnitProfileCode,
            item.Dimensions is { } d ? new DimensionsDto(d.LengthMm, d.WidthMm, d.HeightMm, d.VolumeCBM) : null,
            item.WeightKg,
            new QuantityDto(item.Quantity.Value, item.Quantity.Uom.ToString()),
            item.Hazmat is { } hz ? new HazmatDto(hz.ClassCode, hz.PackingGroup) : null,
            item.Temperature is { } tr ? new TemperatureRangeDto(tr.MinC, tr.MaxC) : null,
            item.HandlingInstructions,
            item.Status
        );

        return Result<ItemDetailDto>.Success(dto);
    }
}
