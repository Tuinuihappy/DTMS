using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;

public record OrderLineDto(
    Guid Id,
    int WorkOrderId,
    string WorkOrder,
    int ItemId,
    string ItemNumber,
    string ItemDescription,
    double Quantity,
    double Weight,
    string? Remarks,
    string ItemStatus);

public record DeliveryLegDto(
    Guid Id,
    int Sequence,
    string PickupLocationCode,
    string DropLocationCode,
    Guid? PickupStationId,
    Guid? DropStationId,
    IReadOnlyList<OrderLineDto> Lines);

public record DeliveryOrderDto(
    Guid Id,
    string OrderKey,
    string Priority,
    string Status,
    DateTime? SLA,
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
            order.OrderKey,
            order.Priority.ToString(),
            order.Status.ToString(),
            order.SLA,
            order.Legs
                .OrderBy(l => l.Sequence)
                .Select(l => new DeliveryLegDto(
                    l.Id,
                    l.Sequence,
                    l.PickupLocationCode,
                    l.DropLocationCode,
                    l.PickupStationId,
                    l.DropStationId,
                    l.OrderLines.Select(ol => new OrderLineDto(
                        ol.Id, ol.WorkOrderId, ol.WorkOrder, ol.ItemId, ol.ItemNumber,
                        ol.ItemDescription, ol.Quantity, ol.Weight, ol.Remarks,
                        ol.ItemStatus.ToString())).ToList()))
                .ToList());
}
