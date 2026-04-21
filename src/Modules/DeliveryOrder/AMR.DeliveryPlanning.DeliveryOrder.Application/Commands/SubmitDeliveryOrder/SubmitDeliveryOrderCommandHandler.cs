using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public class SubmitDeliveryOrderCommandHandler : ICommandHandler<SubmitDeliveryOrderCommand, Guid>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IEventBus _eventBus;

    public SubmitDeliveryOrderCommandHandler(IDeliveryOrderRepository repository, IEventBus eventBus)
    {
        _repository = repository;
        _eventBus = eventBus;
    }

    public async Task<Result<Guid>> Handle(SubmitDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = new Domain.Entities.DeliveryOrder(
            request.OrderKey,
            request.PickupLocationCode,
            request.DropLocationCode,
            request.Priority,
            request.SLA);

        foreach (var line in request.Lines)
        {
            order.AddOrderLine(line.ItemCode, line.Quantity, line.Weight, line.Remarks);
        }

        if (request.Schedule != null)
        {
            order.SetRecurringSchedule(request.Schedule.CronExpression, request.Schedule.ValidFrom, request.Schedule.ValidUntil);
        }

        await _repository.AddAsync(order, cancellationToken);

        // Publish integration event → Planning module auto-creates a Job
        await _eventBus.PublishAsync(new DeliveryOrderReadyForPlanningIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            order.Id,
            request.Priority.ToString(),
            Guid.Parse(request.PickupLocationCode),
            Guid.Parse(request.DropLocationCode)), cancellationToken);

        return Result<Guid>.Success(order.Id);
    }
}

