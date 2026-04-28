using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public class BulkSubmitDeliveryOrdersCommandHandler : ICommandHandler<BulkSubmitDeliveryOrdersCommand, List<Guid>>
{
    private readonly IDeliveryOrderRepository _repo;
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    public BulkSubmitDeliveryOrdersCommandHandler(IDeliveryOrderRepository repo, IEventBus eventBus, ITenantContext tenantContext)
    {
        _repo = repo;
        _eventBus = eventBus;
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

            orders.Add(order);
        }

        await _repo.AddRangeAsync(orders, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        foreach (var order in orders)
        {
            await _eventBus.PublishAsync(new DeliveryOrderReadyForPlanningIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow,
                _tenantContext.TenantId,
                order.Id, order.Priority.ToString(),
                Guid.Parse(order.PickupLocationCode),
                Guid.Parse(order.DropLocationCode)), cancellationToken);
        }

        return Result<List<Guid>>.Success(orders.Select(o => o.Id).ToList());
    }
}
