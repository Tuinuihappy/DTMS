using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;

public record DimensionsDto(double LengthCm, double WidthCm, double HeightCm);

public record QuantityDto(double Value, string Uom);

public record CargoSpecificDto(
    string? PartNo,
    string? Vendor,
    string? DateCode,
    string? TradingCode,
    string? InventoryNo,
    string? Po,
    string? TraceId);

public record ItemDto(
    Guid Id,
    string Sku,
    string PickupLocationCode,
    string DropLocationCode,
    DimensionsDto? Dimensions,
    double WeightKg,
    QuantityDto Quantity,
    CargoSpecificDto? CargoSpecific,
    string Status);

public record DeliveryOrderListDto(
    Guid Id,
    string OrderRef,
    string Priority,
    string CargoType,
    string OrderStatus,
    DateTime? RequestedTime,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    double TotalWeightKg,
    double TotalQuantity,
    int TotalItems);

public record DeliveryOrderDetailDto(
    Guid Id,
    string OrderRef,
    string Priority,
    string CargoType,
    string OrderStatus,
    DateTime? RequestedTime,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
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
        var order = await _repo.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null) return Result<DeliveryOrderDetailDto>.Failure($"Order {request.OrderId} not found.");

        return Result<DeliveryOrderDetailDto>.Success(DeliveryOrderMapper.MapToDetailDto(order));
    }
}

public record GetDeliveryOrdersQuery(OrderStatus? Status, int Page = 1, int PageSize = 20) : IQuery<List<DeliveryOrderListDto>>;

public class GetDeliveryOrdersQueryHandler : IQueryHandler<GetDeliveryOrdersQuery, List<DeliveryOrderListDto>>
{
    private readonly IDeliveryOrderRepository _repo;
    public GetDeliveryOrdersQueryHandler(IDeliveryOrderRepository repo) => _repo = repo;

    public async Task<Result<List<DeliveryOrderListDto>>> Handle(GetDeliveryOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = request.Status.HasValue
            ? await _repo.GetByStatusAsync(request.Status.Value, request.Page, request.PageSize, cancellationToken)
            : await _repo.GetAllAsync(request.Page, request.PageSize, cancellationToken);

        return Result<List<DeliveryOrderListDto>>.Success(orders.Select(DeliveryOrderMapper.MapToListDto).ToList());
    }
}

file static class DeliveryOrderMapper
{
    public static DeliveryOrderListDto MapToListDto(Domain.Entities.DeliveryOrder order) =>
        new(
            order.Id,
            order.OrderRef,
            order.Priority.ToString(),
            order.CargoType.ToString(),
            order.Status.ToString(),
            order.RequestedTime,
            order.CreatedAt,
            order.UpdatedAt,
            order.TotalWeightKg,
            order.TotalQuantity,
            order.TotalItems);

    public static DeliveryOrderDetailDto MapToDetailDto(Domain.Entities.DeliveryOrder order) =>
        new(
            order.Id,
            order.OrderRef,
            order.Priority.ToString(),
            order.CargoType.ToString(),
            order.Status.ToString(),
            order.RequestedTime,
            order.CreatedAt,
            order.UpdatedAt,
            order.TotalWeightKg,
            order.TotalQuantity,
            order.TotalItems,
            order.Items.Select(p => new ItemDto(
                p.Id,
                p.Sku,
                p.PickupLocationCode,
                p.DropLocationCode,
                p.Dimensions is { } d ? new DimensionsDto(d.LengthCm, d.WidthCm, d.HeightCm) : null,
                p.WeightKg,
                new QuantityDto(p.Quantity, p.Uom),
                p.CargoSpecific is { } cs
                    ? new CargoSpecificDto(cs.PartNo, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId)
                    : null,
                p.Status.ToString()
            )).ToList());
}
