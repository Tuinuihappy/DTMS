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
    LocationRefDto PickupLocation,
    LocationRefDto DropLocation,
    Guid? PickupStationId,
    Guid? DropStationId,
    string? LoadUnitProfileCode,
    DimensionsDto? Dimensions,
    double? WeightKg,
    QuantityDto Quantity,
    CargoType? CargoType,
    CargoSpecificDto? CargoSpecific,
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
            new LocationRefDto(item.PickupLocation.Code, item.PickupLocation.StationId),
            new LocationRefDto(item.DropLocation.Code, item.DropLocation.StationId),
            item.PickupStationId,
            item.DropStationId,
            item.LoadUnitProfileCode,
            item.Dimensions is { } d ? new DimensionsDto(d.LengthMm, d.WidthMm, d.HeightMm, d.VolumeCBM) : null,
            item.WeightKg,
            new QuantityDto(item.Quantity, item.Uom),
            item.CargoType,
            item.CargoSpecific is { } cs
                ? new CargoSpecificDto(cs.PartNo, cs.Wo, cs.Line, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                : null,
            item.Status
        );

        return Result<ItemDetailDto>.Success(dto);
    }
}
