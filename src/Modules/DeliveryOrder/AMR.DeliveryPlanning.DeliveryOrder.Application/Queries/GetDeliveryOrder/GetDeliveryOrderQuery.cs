using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;

public record DimensionsDto(double LengthMm, double WidthMm, double HeightMm, double VolumeCBM);

public record QuantityDto(double Value, string Uom);

public record ServiceWindowDto(DateTime? EarliestUtc, DateTime? LatestUtc);

public record HazmatDto(string ClassCode, PackingGroup? PackingGroup);

public record TemperatureRangeDto(double? MinC, double? MaxC);

public record ItemDto(
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
    ItemStatus Status);

public record DeliveryOrderListDto(
    Guid Id,
    string OrderRef,
    SourceSystem SourceSystem,
    Priority Priority,
    OrderStatus OrderStatus,
    ServiceWindowDto? ServiceWindow,
    DateTime? SubmittedAt,
    string? CreatedBy,
    string? RequestedBy,
    string? Notes,
    DateTime CreatedDate,
    DateTime? UpdatedDate,
    double TotalWeightKg,
    double TotalQuantity,
    int TotalItems,
    TransportMode? RequestedTransportMode);

public record DeliveryOrderDetailDto(
    Guid Id,
    string OrderRef,
    SourceSystem SourceSystem,
    Priority Priority,
    OrderStatus OrderStatus,
    ServiceWindowDto? ServiceWindow,
    DateTime? SubmittedAt,
    string? CreatedBy,
    string? RequestedBy,
    string? Notes,
    DateTime CreatedDate,
    DateTime? UpdatedDate,
    double TotalWeightKg,
    double TotalQuantity,
    int TotalItems,
    TransportMode? RequestedTransportMode,
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
            order.ServiceWindow is { } sw ? new ServiceWindowDto(sw.EarliestUtc, sw.LatestUtc) : null,
            order.SubmittedAt,
            order.CreatedBy,
            order.RequestedBy,
            order.Notes,
            order.CreatedDate,
            order.UpdatedDate,
            order.TotalWeightKg,
            order.TotalQuantity,
            order.TotalItems,
            order.RequestedTransportMode);

    public static DeliveryOrderDetailDto MapToDetailDto(Domain.Entities.DeliveryOrder order) =>
        new(
            order.Id,
            order.OrderRef,
            order.SourceSystem,
            order.Priority,
            order.Status,
            order.ServiceWindow is { } sw ? new ServiceWindowDto(sw.EarliestUtc, sw.LatestUtc) : null,
            order.SubmittedAt,
            order.CreatedBy,
            order.RequestedBy,
            order.Notes,
            order.CreatedDate,
            order.UpdatedDate,
            order.TotalWeightKg,
            order.TotalQuantity,
            order.TotalItems,
            order.RequestedTransportMode,
            order.Items.Select(p => new ItemDto(
                p.Id,
                p.ItemSeq,
                p.ItemId,
                p.Description,
                p.PickupLocationCode,
                p.DropLocationCode,
                p.PickupStationId,
                p.DropStationId,
                p.LoadUnitProfileCode,
                p.Dimensions is { } d ? new DimensionsDto(d.LengthMm, d.WidthMm, d.HeightMm, d.VolumeCBM) : null,
                p.WeightKg,
                new QuantityDto(p.Quantity.Value, p.Quantity.Uom.ToString()),
                p.Hazmat is { } hz ? new HazmatDto(hz.ClassCode, hz.PackingGroup) : null,
                p.Temperature is { } tr ? new TemperatureRangeDto(tr.MinC, tr.MaxC) : null,
                p.HandlingInstructions,
                p.Status
            )).ToList());
}
