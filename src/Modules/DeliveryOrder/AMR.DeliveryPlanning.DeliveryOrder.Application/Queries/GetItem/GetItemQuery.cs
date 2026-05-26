using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetItem;

public record ItemDetailDto(
    Guid Id,
    Guid DeliveryOrderId,
    int ItemSeq,
    string Sku,
    string? Description,
    string PickupLocationCode,
    string DropLocationCode,
    Guid? PickupStationId,
    Guid? DropStationId,
    string? LoadUnitProfileCode,
    DimensionsDto? Dimensions,
    double? WeightKg,
    QuantityDto Quantity,
    CargoType? CargoType,
    CargoSpecificDto? CargoSpecific,
    HazmatDto? Hazmat,
    TemperatureRangeDto? Temperature,
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
            item.Sku,
            item.Description,
            item.PickupLocationCode,
            item.DropLocationCode,
            item.PickupStationId,
            item.DropStationId,
            item.LoadUnitProfileCode,
            item.Dimensions is { } d ? new DimensionsDto(d.LengthMm, d.WidthMm, d.HeightMm, d.VolumeCBM) : null,
            item.WeightKg,
            new QuantityDto(item.Quantity.Value, item.Quantity.Uom.ToString()),
            item.CargoType,
            item.CargoSpecific is { } cs
                ? new CargoSpecificDto(cs.PartNo, cs.Wo, cs.Line, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                : null,
            item.Hazmat is { } hz ? new HazmatDto(hz.ClassCode, hz.PackingGroup) : null,
            item.Temperature is { } tr ? new TemperatureRangeDto(tr.MinC, tr.MaxC) : null,
            item.Status
        );

        return Result<ItemDetailDto>.Success(dto);
    }
}
