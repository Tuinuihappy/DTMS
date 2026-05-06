using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;

public record PackageContentDto(string ItemNumber, double Quantity);

public record PackageUnitDto(
    Guid Id,
    string Barcode,
    string LoadUnitProfileCode,
    double GrossWeightKg,
    string Status,
    IReadOnlyList<PackageContentDto> Contents);

public record DeliveryLegDto(
    Guid Id,
    int Sequence,
    string PickupLocationCode,
    string DropLocationCode,
    string CarrierTypeCode,
    Guid? PickupStationId,
    Guid? DropStationId,
    IReadOnlyList<PackageUnitDto> Packages);

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
                    l.CarrierTypeCode,
                    l.PickupStationId,
                    l.DropStationId,
                    l.Packages.Select(p => new PackageUnitDto(
                        p.Id,
                        p.Barcode,
                        p.LoadUnitProfileCode,
                        p.GrossWeightKg,
                        p.Status.ToString(),
                        p.Contents.Select(c => new PackageContentDto(c.ItemNumber, c.Quantity)).ToList()
                    )).ToList()))
                .ToList());
}
