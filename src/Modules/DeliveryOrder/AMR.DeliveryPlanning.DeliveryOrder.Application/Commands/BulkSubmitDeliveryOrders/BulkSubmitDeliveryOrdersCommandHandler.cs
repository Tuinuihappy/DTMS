using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public class BulkSubmitDeliveryOrdersCommandHandler : ICommandHandler<BulkSubmitDeliveryOrdersCommand, List<Guid>>
{
    private readonly IDeliveryOrderRepository _repo;
    private readonly ITenantContext _tenantContext;

    public BulkSubmitDeliveryOrdersCommandHandler(IDeliveryOrderRepository repo, ITenantContext tenantContext)
    {
        _repo = repo;
        _tenantContext = tenantContext;
    }

    public async Task<Result<List<Guid>>> Handle(BulkSubmitDeliveryOrdersCommand request, CancellationToken cancellationToken)
    {
        if (request.Orders.Count == 0)
            return Result<List<Guid>>.Failure("No orders provided.");

        var orders = new List<Domain.Entities.DeliveryOrder>();

        foreach (var cmd in request.Orders)
        {
            var order = new Domain.Entities.DeliveryOrder(
                _tenantContext.TenantId, cmd.OrderKey, cmd.PickupLocationCode, cmd.DropLocationCode, cmd.Priority, cmd.SLA);

            foreach (var line in cmd.Lines)
                order.AddOrderLine(line.ItemCode, line.Quantity, line.Weight, line.Remarks);

            if (cmd.Schedule != null)
                order.SetRecurringSchedule(cmd.Schedule.CronExpression, cmd.Schedule.ValidFrom, cmd.Schedule.ValidUntil);

            if (!Guid.TryParse(cmd.PickupLocationCode, out var pickupStationId))
                return Result<List<Guid>>.Failure($"PickupLocationCode '{cmd.PickupLocationCode}' is not a valid station ID (Guid).");

            if (!Guid.TryParse(cmd.DropLocationCode, out var dropStationId))
                return Result<List<Guid>>.Failure($"DropLocationCode '{cmd.DropLocationCode}' is not a valid station ID (Guid).");

            order.MarkAsValidated(pickupStationId, dropStationId);
            order.MarkReadyToPlan();

            orders.Add(order);
        }

        await _repo.AddRangeAsync(orders, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result<List<Guid>>.Success(orders.Select(o => o.Id).ToList());
    }
}
