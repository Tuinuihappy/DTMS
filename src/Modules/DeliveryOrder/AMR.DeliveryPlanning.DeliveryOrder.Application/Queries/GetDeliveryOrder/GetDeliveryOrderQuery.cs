using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;

public record DimensionsDto(double LengthMm, double WidthMm, double HeightMm, double VolumeM3);

public record QuantityDto(double Value, string Uom);

public record CargoSpecificDto(
    string? PartNo,
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
    string PickupLocationCode,
    string DropLocationCode,
    CargoType CargoType,
    DimensionsDto? Dimensions,
    double WeightKg,
    QuantityDto Quantity,
    CargoSpecificDto? CargoSpecific,
    string Status);

public record DeliveryOrderListDto(
    Guid Id,
    string OrderRef,
    string SourceSystem,
    string Priority,
    string OrderStatus,
    DateTime? RequestedDeliveryDate,
    DateTime CreatedDate,
    DateTime? UpdatedDate,
    double TotalWeightKg,
    double TotalQuantity,
    int TotalItems);

public record DeliveryOrderDetailDto(
    Guid Id,
    string OrderRef,
    string SourceSystem,
    string Priority,
    string OrderStatus,
    DateTime? RequestedDeliveryDate,
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
        var dataTask  = request.Status.HasValue
            ? _repo.GetByStatusAsync(request.Status.Value, request.Page, request.PageSize, cancellationToken)
            : _repo.GetAllAsync(request.Page, request.PageSize, cancellationToken);

        var countTask = _repo.CountAsync(request.Status, cancellationToken);

        await Task.WhenAll(dataTask, countTask);

        var paged = new PagedResult<DeliveryOrderListDto>(
            dataTask.Result.Select(DeliveryOrderMapper.MapToListDto).ToList(),
            countTask.Result,
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
            order.SourceSystem.ToString(),
            order.Priority.ToString(),
            order.Status.ToString(),
            order.RequestedDeliveryDate,
            order.CreatedDate,
            order.UpdatedDate,
            order.TotalWeightKg,
            order.TotalQuantity,
            order.TotalItems);

    public static DeliveryOrderDetailDto MapToDetailDto(Domain.Entities.DeliveryOrder order) =>
        new(
            order.Id,
            order.OrderRef,
            order.SourceSystem.ToString(),
            order.Priority.ToString(),
            order.Status.ToString(),
            order.RequestedDeliveryDate,
            order.CreatedDate,
            order.UpdatedDate,
            order.TotalWeightKg,
            order.TotalQuantity,
            order.TotalItems,
            order.Items.Select(p => new ItemDto(
                p.Id,
                p.ItemSeq,
                p.Sku,
                p.PickupLocationCode,
                p.DropLocationCode,
                p.CargoType,
                p.Dimensions is { } d ? new DimensionsDto(d.LengthMm, d.WidthMm, d.HeightMm, d.VolumeM3) : null,
                p.WeightKg,
                new QuantityDto(p.Quantity, p.Uom),
                p.CargoSpecific is { } cs
                    ? new CargoSpecificDto(cs.PartNo, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                    : null,
                p.Status.ToString()
            )).ToList());
}
