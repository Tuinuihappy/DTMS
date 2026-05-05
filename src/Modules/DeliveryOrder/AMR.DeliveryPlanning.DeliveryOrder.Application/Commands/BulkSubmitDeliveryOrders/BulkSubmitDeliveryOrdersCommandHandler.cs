using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public class BulkSubmitDeliveryOrdersCommandHandler : ICommandHandler<BulkSubmitDeliveryOrdersCommand, List<Guid>>
{
    private readonly IDeliveryOrderRepository _repo;
    private readonly StationValidationService _stationValidation;
    private readonly ITenantContext _tenantContext;

    public BulkSubmitDeliveryOrdersCommandHandler(
        IDeliveryOrderRepository repo,
        StationValidationService stationValidation,
        ITenantContext tenantContext)
    {
        _repo = repo;
        _stationValidation = stationValidation;
        _tenantContext = tenantContext;
    }

    public async Task<Result<List<Guid>>> Handle(BulkSubmitDeliveryOrdersCommand request, CancellationToken cancellationToken)
    {
        if (request.Orders.Count == 0)
            return Result<List<Guid>>.Failure("No orders provided.");

        var orders = new List<Domain.Entities.DeliveryOrder>();

        foreach (var cmd in request.Orders)
        {
            var stationMap = await BuildStationMapAsync(cmd.OrderItems, cancellationToken);
            if (stationMap.IsFailure) return Result<List<Guid>>.Failure(stationMap.Error);

            var order = new Domain.Entities.DeliveryOrder(
                _tenantContext.TenantId, cmd.OrderId, cmd.OrderNo, cmd.CreateBy, cmd.Priority, cmd.SLA);

            foreach (var line in cmd.OrderItems)
                order.AddOrderItem(line.PickupLocationCode, line.DropLocationCode,
                    line.WorkOrderId, line.WorkOrder, line.ItemId, line.ItemNumber,
                    line.ItemDescription, line.Quantity, line.Weight, line.Line, line.Model, line.Remarks);

            if (cmd.Schedule != null)
                order.SetRecurringSchedule(cmd.Schedule.CronExpression, cmd.Schedule.ValidFrom, cmd.Schedule.ValidUntil);

            order.MarkAsValidated(stationMap.Value);
            order.MarkReadyToPlan();

            orders.Add(order);
        }

        await _repo.AddRangeAsync(orders, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result<List<Guid>>.Success(orders.Select(o => o.Id).ToList());
    }

    private async Task<Result<IReadOnlyDictionary<(string pickup, string drop), (Guid pickupStationId, Guid dropStationId)>>>
        BuildStationMapAsync(IEnumerable<OrderItemDto> lines, CancellationToken cancellationToken)
    {
        var uniquePairs = lines
            .Select(l => (l.PickupLocationCode, l.DropLocationCode))
            .Distinct()
            .ToList();

        var map = new Dictionary<(string, string), (Guid, Guid)>();

        foreach (var (pickup, drop) in uniquePairs)
        {
            var (pickupOk, pickupStationId, pickupErr) = await _stationValidation.ResolveAndValidateAsync(
                pickup, "PickupLocationCode", cancellationToken);
            if (!pickupOk) return Result<IReadOnlyDictionary<(string, string), (Guid, Guid)>>.Failure(pickupErr!);

            var (dropOk, dropStationId, dropErr) = await _stationValidation.ResolveAndValidateAsync(
                drop, "DropLocationCode", cancellationToken);
            if (!dropOk) return Result<IReadOnlyDictionary<(string, string), (Guid, Guid)>>.Failure(dropErr!);

            map[(pickup, drop)] = (pickupStationId, dropStationId);
        }

        return Result<IReadOnlyDictionary<(string, string), (Guid, Guid)>>.Success(map);
    }
}
