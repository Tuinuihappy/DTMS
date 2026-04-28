using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public class SubmitDeliveryOrderCommandHandler : ICommandHandler<SubmitDeliveryOrderCommand, Guid>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IEventBus _eventBus;
    private readonly IStationLookup _stationLookup;
    private readonly ITenantContext _tenantContext;

    public SubmitDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IEventBus eventBus,
        IStationLookup stationLookup,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _eventBus = eventBus;
        _stationLookup = stationLookup;
        _tenantContext = tenantContext;
    }

    public async Task<Result<Guid>> Handle(SubmitDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.PickupLocationCode, out var pickupStationId))
            return Result<Guid>.Failure($"PickupLocationCode '{request.PickupLocationCode}' is not a valid station ID (Guid).");

        if (!Guid.TryParse(request.DropLocationCode, out var dropStationId))
            return Result<Guid>.Failure($"DropLocationCode '{request.DropLocationCode}' is not a valid station ID (Guid).");

        // Validate both stations exist in the Facility module before persisting
        if (!await _stationLookup.ExistsAsync(pickupStationId, cancellationToken))
            return Result<Guid>.Failure($"Pickup station '{pickupStationId}' does not exist.");

        if (!await _stationLookup.ExistsAsync(dropStationId, cancellationToken))
            return Result<Guid>.Failure($"Drop station '{dropStationId}' does not exist.");

        var order = new Domain.Entities.DeliveryOrder(
            _tenantContext.TenantId,
            request.OrderKey,
            request.PickupLocationCode,
            request.DropLocationCode,
            request.Priority,
            request.SLA);

        foreach (var line in request.Lines)
            order.AddOrderLine(line.ItemCode, line.Quantity, line.Weight, line.Remarks);

        if (request.Schedule != null)
            order.SetRecurringSchedule(request.Schedule.CronExpression, request.Schedule.ValidFrom, request.Schedule.ValidUntil);

        // Resolve codes → station IDs and advance through the domain status flow
        order.MarkAsValidated(pickupStationId, dropStationId);
        order.MarkReadyToPlan();

        await _repository.AddAsync(order, cancellationToken);

        await _eventBus.PublishAsync(new DeliveryOrderReadyForPlanningIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            _tenantContext.TenantId,
            order.Id,
            request.Priority.ToString(),
            pickupStationId,
            dropStationId), cancellationToken);

        return Result<Guid>.Success(order.Id);
    }
}
