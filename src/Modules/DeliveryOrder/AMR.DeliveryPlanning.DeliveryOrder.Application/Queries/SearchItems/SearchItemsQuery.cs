using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.SearchItems;

public record OrderContextDto(Guid Id, string OrderRef, string OrderStatus, string Priority);

public record ItemSearchResultDto(
    Guid Id,
    int ItemSeq,
    string Sku,
    string PickupLocationCode,
    string DropLocationCode,
    string CargoType,
    DimensionsDto? Dimensions,
    double WeightKg,
    QuantityDto Quantity,
    CargoSpecificDto? CargoSpecific,
    string Status,
    OrderContextDto Order);

public record SearchItemsQuery(
    string? Sku,
    CargoType? CargoType,
    ItemStatus? Status,
    string? PickupLocationCode,
    string? DropLocationCode,
    string? PartNo,
    string? Vendor,
    string? DateCode,
    string? TradingCode,
    string? InventoryNo,
    string? Po,
    string? TraceId,
    string? LotNo,
    int Page = 1,
    int PageSize = 20
) : IQuery<PagedResult<ItemSearchResultDto>>;

public class SearchItemsQueryHandler : IQueryHandler<SearchItemsQuery, PagedResult<ItemSearchResultDto>>
{
    private readonly IDeliveryOrderRepository _repo;
    public SearchItemsQueryHandler(IDeliveryOrderRepository repo) => _repo = repo;

    public async Task<Result<PagedResult<ItemSearchResultDto>>> Handle(SearchItemsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        var (items, totalCount) = await _repo.SearchItemsAsync(
            request.Sku, request.CargoType, request.Status,
            request.PickupLocationCode, request.DropLocationCode,
            request.PartNo, request.Vendor, request.DateCode, request.TradingCode,
            request.InventoryNo, request.Po, request.TraceId, request.LotNo,
            page, pageSize, cancellationToken);

        var orderIds = items.Select(i => i.DeliveryOrderId).Distinct();
        var orders = await _repo.GetByIdsAsync(orderIds, cancellationToken);
        var orderMap = orders.ToDictionary(o => o.Id);

        var data = items
            .Where(i => orderMap.ContainsKey(i.DeliveryOrderId))
            .Select(i =>
            {
                var o = orderMap[i.DeliveryOrderId];
                return new ItemSearchResultDto(
                    i.Id,
                    i.ItemSeq,
                    i.Sku,
                    i.PickupLocationCode,
                    i.DropLocationCode,
                    i.CargoType.ToString(),
                    i.Dimensions is { } d ? new DimensionsDto(d.LengthMm, d.WidthMm, d.HeightMm, d.VolumeM3) : null,
                    i.WeightKg,
                    new QuantityDto(i.Quantity, i.Uom),
                    i.CargoSpecific is { } cs
                        ? new CargoSpecificDto(cs.PartNo, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                        : null,
                    i.Status.ToString(),
                    new OrderContextDto(o.Id, o.OrderRef, o.Status.ToString(), o.Priority.ToString())
                );
            })
            .ToList();

        return Result<PagedResult<ItemSearchResultDto>>.Success(
            new PagedResult<ItemSearchResultDto>(data, totalCount, page, pageSize));
    }
}
