using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;

public record DimsDto(double LengthMm, double WidthMm, double HeightMm);
public record TemperatureRangeDto(double? MinCelsius, double? MaxCelsius);

public record OrderItemDto(
    Guid Id,
    string? WorkOrder,
    string ItemNumber,
    string ItemDescription,
    double Quantity,
    double? Weight,
    string LoadUnitType,
    DimsDto? Dims,
    int? HazmatClass,
    TemperatureRangeDto? TemperatureRange,
    List<string> HandlingInstructions,
    string? Line,
    string? Model,
    string? Remarks,
    string ItemStatus);

public record DeliveryLegDto(
    Guid Id,
    int Sequence,
    string PickupLocationCode,
    string DropLocationCode,
    Guid? PickupStationId,
    Guid? DropStationId,
    IReadOnlyList<OrderItemDto> OrderItems);

public record ServiceWindowDto(DateTime? Earliest, DateTime? Latest);

public record DeliveryOrderDto(
    Guid Id,
    string OrderName,
    string SlaTier,
    string StructureType,
    string Status,
    ServiceWindowDto ServiceWindow,
    List<string> Tags,
    IReadOnlyList<DeliveryLegDto> Legs);

public record GetDeliveryOrderQuery(Guid OrderId) : IQuery<DeliveryOrderDto>;

public class GetDeliveryOrderQueryHandler : IQueryHandler<GetDeliveryOrderQuery, DeliveryOrderDto>
{
    private readonly IDeliveryOrderRepository _repo;
    public GetDeliveryOrderQueryHandler(IDeliveryOrderRepository repo) => _repo = repo;

    public async Task<Result<DeliveryOrderDto>> Handle(GetDeliveryOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await _repo.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null) return Result<DeliveryOrderDto>.Failure($"Order {request.OrderId} not found.");

        return Result<DeliveryOrderDto>.Success(DeliveryOrderMapper.MapToDto(order));
    }
}

public record GetDeliveryOrdersQuery(OrderStatus? Status, int Page = 1, int PageSize = 20) : IQuery<List<DeliveryOrderDto>>;

public class GetDeliveryOrdersQueryHandler : IQueryHandler<GetDeliveryOrdersQuery, List<DeliveryOrderDto>>
{
    private readonly IDeliveryOrderRepository _repo;
    public GetDeliveryOrdersQueryHandler(IDeliveryOrderRepository repo) => _repo = repo;

    public async Task<Result<List<DeliveryOrderDto>>> Handle(GetDeliveryOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = request.Status.HasValue
            ? await _repo.GetByStatusAsync(request.Status.Value, request.Page, request.PageSize, cancellationToken)
            : await _repo.GetAllAsync(request.Page, request.PageSize, cancellationToken);

        return Result<List<DeliveryOrderDto>>.Success(orders.Select(DeliveryOrderMapper.MapToDto).ToList());
    }
}

file static class DeliveryOrderMapper
{
    public static DeliveryOrderDto MapToDto(Domain.Entities.DeliveryOrder order) =>
        new(
            order.Id,
            order.OrderName,
            order.SlaTier.ToString(),
            order.StructureType.ToString(),
            order.Status.ToString(),
            new ServiceWindowDto(order.ServiceWindow.Earliest, order.ServiceWindow.Latest),
            order.Tags,
            order.Legs
                .OrderBy(l => l.Sequence)
                .Select(l => new DeliveryLegDto(
                    l.Id,
                    l.Sequence,
                    l.PickupLocationCode,
                    l.DropLocationCode,
                    l.PickupStationId,
                    l.DropStationId,
                    l.OrderItems.Select(ol => new OrderItemDto(
                        ol.Id, ol.WorkOrder, ol.ItemNumber, ol.ItemDescription,
                        ol.Quantity, ol.Weight,
                        ol.LoadUnitType.ToString(),
                        ol.Dims is null ? null : new DimsDto(ol.Dims.LengthMm, ol.Dims.WidthMm, ol.Dims.HeightMm),
                        ol.HazmatClass,
                        ol.TemperatureRange is null ? null : new TemperatureRangeDto(ol.TemperatureRange.MinCelsius, ol.TemperatureRange.MaxCelsius),
                        ol.HandlingInstructions.Select(h => h.ToString()).ToList(),
                        ol.Line, ol.Model, ol.Remarks,
                        ol.ItemStatus.ToString())).ToList()))
                .ToList());
}
