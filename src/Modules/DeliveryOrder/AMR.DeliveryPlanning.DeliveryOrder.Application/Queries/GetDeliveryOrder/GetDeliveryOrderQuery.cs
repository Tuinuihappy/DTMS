using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;

public record DeliveryOrderDto(
    Guid Id, string OrderKey, string PickupLocationCode, string DropLocationCode,
    string Priority, string Status, DateTime? SLA, Guid? PickupStationId,
    int LineCount);

public record GetDeliveryOrderQuery(Guid OrderId) : IQuery<DeliveryOrderDto>;

public class GetDeliveryOrderQueryHandler : IQueryHandler<GetDeliveryOrderQuery, DeliveryOrderDto>
{
    private readonly IDeliveryOrderRepository _repo;
    public GetDeliveryOrderQueryHandler(IDeliveryOrderRepository repo) => _repo = repo;

    public async Task<Result<DeliveryOrderDto>> Handle(GetDeliveryOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await _repo.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null) return Result<DeliveryOrderDto>.Failure($"Order {request.OrderId} not found.");

        return Result<DeliveryOrderDto>.Success(new DeliveryOrderDto(
            order.Id, order.OrderKey, order.PickupLocationCode, order.DropLocationCode,
            order.Priority.ToString(), order.Status.ToString(), order.SLA,
            order.PickupStationId,
            order.OrderLines.Count));
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
            : await _repo.GetByStatusAsync(OrderStatus.Submitted, request.Page, request.PageSize, cancellationToken);

        var dtos = orders.Select(o => new DeliveryOrderDto(
            o.Id, o.OrderKey, o.PickupLocationCode, o.DropLocationCode,
            o.Priority.ToString(), o.Status.ToString(), o.SLA, o.PickupStationId,
            o.OrderLines.Count)).ToList();

        return Result<List<DeliveryOrderDto>>.Success(dtos);
    }
}
