using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.SearchItems;

public record OrderContextDto(Guid Id, string OrderRef, OrderStatus OrderStatus, Priority Priority);

public record ItemSearchResultDto(
    Guid Id,
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
    ItemStatus Status,
    OrderContextDto Order);

public record SearchItemsQuery(
    string? ItemId,
    ItemStatus? Status,
    string? PickupCode,
    Guid? PickupStationId,
    string? DropCode,
    Guid? DropStationId,
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
            request.ItemId, request.Status,
            request.PickupCode, request.PickupStationId,
            request.DropCode, request.DropStationId,
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
                    i.ItemId,
                    i.Description,
                    i.PickupLocationCode,
                    i.DropLocationCode,
                    i.PickupStationId,
                    i.DropStationId,
                    i.LoadUnitProfileCode,
                    i.Dimensions is { } d ? new DimensionsDto(d.LengthMm, d.WidthMm, d.HeightMm, d.VolumeCBM) : null,
                    i.WeightKg,
                    new QuantityDto(i.Quantity.Value, i.Quantity.Uom.ToString()),
                    i.Hazmat is { } hz ? new HazmatDto(hz.ClassCode, hz.PackingGroup) : null,
                    i.Temperature is { } tr ? new TemperatureRangeDto(tr.MinC, tr.MaxC) : null,
                    i.HandlingInstructions,
                    i.Status,
                    new OrderContextDto(o.Id, o.OrderRef, o.Status, o.Priority)
                );
            })
            .ToList();

        return Result<PagedResult<ItemSearchResultDto>>.Success(
            new PagedResult<ItemSearchResultDto>(data, totalCount, page, pageSize));
    }
}
