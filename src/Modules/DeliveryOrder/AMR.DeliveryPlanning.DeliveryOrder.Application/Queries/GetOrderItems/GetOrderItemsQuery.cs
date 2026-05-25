using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetOrderItems;

public record GetOrderItemsQuery(Guid OrderId, ItemStatus? Status = null)
    : IQuery<IReadOnlyList<ItemDto>>;

public class GetOrderItemsQueryHandler : IQueryHandler<GetOrderItemsQuery, IReadOnlyList<ItemDto>>
{
    private readonly IDeliveryOrderRepository _repo;
    public GetOrderItemsQueryHandler(IDeliveryOrderRepository repo) => _repo = repo;

    public async Task<Result<IReadOnlyList<ItemDto>>> Handle(GetOrderItemsQuery request, CancellationToken cancellationToken)
    {
        var order = await _repo.GetByIdAsNoTrackingAsync(request.OrderId, cancellationToken);
        if (order == null)
            return Result<IReadOnlyList<ItemDto>>.Failure($"Order {request.OrderId} not found.");

        var items = order.Items
            .Where(i => !request.Status.HasValue || i.Status == request.Status.Value)
            .OrderBy(i => i.ItemSeq)
            .Select(p => new ItemDto(
                p.Id,
                p.ItemSeq,
                p.Sku,
                p.Description,
                p.PickupLocationCode,
                p.DropLocationCode,
                p.PickupStationId,
                p.DropStationId,
                p.LoadUnitProfileCode,
                p.Dimensions is { } d ? new DimensionsDto(d.LengthMm, d.WidthMm, d.HeightMm, d.VolumeCBM) : null,
                p.WeightKg,
                new QuantityDto(p.Quantity, p.Uom),
                p.CargoType,
                p.CargoSpecific is { } cs
                    ? new CargoSpecificDto(cs.PartNo, cs.Wo, cs.Line, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                    : null,
                p.Status
            ))
            .ToList();

        return Result<IReadOnlyList<ItemDto>>.Success(items);
    }
}
