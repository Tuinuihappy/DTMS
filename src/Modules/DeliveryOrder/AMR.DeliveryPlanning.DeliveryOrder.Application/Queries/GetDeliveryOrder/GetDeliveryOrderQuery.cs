using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;

public record DimensionsDto(double LengthMm, double WidthMm, double HeightMm, double VolumeCBM);

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

public record ItemDto(
    Guid Id,
    int ItemSeq,
    string Sku,
    string? Description,
    string PickupLocationCode,
    string DropLocationCode,
    CargoType CargoType,
    string? LoadUnitProfileCode,
    DimensionsDto? Dimensions,
    double? WeightKg,
    QuantityDto Quantity,
    CargoSpecificDto? CargoSpecific,
    ItemStatus Status);

public record DeliveryOrderListDto(
    Guid Id,
    string OrderRef,
    SourceSystem SourceSystem,
    Priority Priority,
    OrderStatus OrderStatus,
    DateTime? RequestedDeliveryDate,
    string? CreatedBy,
    DateTime CreatedDate,
    DateTime? UpdatedDate,
    double TotalWeightKg,
    double TotalQuantity,
    int TotalItems);

public record DeliveryOrderDetailDto(
    Guid Id,
    string OrderRef,
    SourceSystem SourceSystem,
    Priority Priority,
    OrderStatus OrderStatus,
    DateTime? RequestedDeliveryDate,
    string? CreatedBy,
    DateTime CreatedDate,
    DateTime? UpdatedDate,
    double TotalWeightKg,
    double TotalQuantity,
    int TotalItems,
    IReadOnlyList<ItemDto> Items);

public record GetDeliveryOrderQuery(Guid OrderId) : IQuery<DeliveryOrderDetailDto>;

public class GetDeliveryOrderQueryHandler : IQueryHandler<GetDeliveryOrderQuery, DeliveryOrderDetailDto>
{
    private readonly IDeliveryOrderRepository _repo;
    public GetDeliveryOrderQueryHandler(IDeliveryOrderRepository repo) => _repo = repo;

    public async Task<Result<DeliveryOrderDetailDto>> Handle(GetDeliveryOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await _repo.GetByIdAsNoTrackingAsync(request.OrderId, cancellationToken);
        if (order == null) return Result<DeliveryOrderDetailDto>.Failure($"Order {request.OrderId} not found.");

        return Result<DeliveryOrderDetailDto>.Success(DeliveryOrderMapper.MapToDetailDto(order));
    }
}

public record GetDeliveryOrdersQuery(OrderStatus? Status, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<DeliveryOrderListDto>>;

public class GetDeliveryOrdersQueryHandler : IQueryHandler<GetDeliveryOrdersQuery, PagedResult<DeliveryOrderListDto>>
{
    private readonly IDeliveryOrderRepository _repo;
    public GetDeliveryOrdersQueryHandler(IDeliveryOrderRepository repo) => _repo = repo;

    public async Task<Result<PagedResult<DeliveryOrderListDto>>> Handle(GetDeliveryOrdersQuery request, CancellationToken cancellationToken)
    {
        var data = request.Status.HasValue
            ? await _repo.GetByStatusAsync(request.Status.Value, request.Page, request.PageSize, cancellationToken)
            : await _repo.GetAllAsync(request.Page, request.PageSize, cancellationToken);

        var count = await _repo.CountAsync(request.Status, cancellationToken);

        var paged = new PagedResult<DeliveryOrderListDto>(
            data.Select(DeliveryOrderMapper.MapToListDto).ToList(),
            count,
            request.Page,
            request.PageSize);

        return Result<PagedResult<DeliveryOrderListDto>>.Success(paged);
    }
}

internal static class DeliveryOrderMapper
{
    public static DeliveryOrderListDto MapToListDto(Domain.Entities.DeliveryOrder order) =>
        new(
            order.Id,
            order.OrderRef,
            order.SourceSystem,
            order.Priority,
            order.Status,
            order.RequestedDeliveryDate,
            order.CreatedBy,
            order.CreatedDate,
            order.UpdatedDate,
            order.TotalWeightKg,
            order.TotalQuantity,
            order.TotalItems);

    public static DeliveryOrderDetailDto MapToDetailDto(Domain.Entities.DeliveryOrder order) =>
        new(
            order.Id,
            order.OrderRef,
            order.SourceSystem,
            order.Priority,
            order.Status,
            order.RequestedDeliveryDate,
            order.CreatedBy,
            order.CreatedDate,
            order.UpdatedDate,
            order.TotalWeightKg,
            order.TotalQuantity,
            order.TotalItems,
            order.Items.Select(p => new ItemDto(
                p.Id,
                p.ItemSeq,
                p.Sku,
                p.Description,
                p.PickupLocationCode,
                p.DropLocationCode,
                p.CargoType,
                p.LoadUnitProfileCode,
                p.Dimensions is { } d ? new DimensionsDto(d.LengthMm, d.WidthMm, d.HeightMm, d.VolumeCBM) : null,
                p.WeightKg,
                new QuantityDto(p.Quantity, p.Uom),
                p.CargoSpecific is { } cs
                    ? new CargoSpecificDto(cs.PartNo, cs.Wo, cs.Line, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                    : null,
                p.Status
            )).ToList());
}
