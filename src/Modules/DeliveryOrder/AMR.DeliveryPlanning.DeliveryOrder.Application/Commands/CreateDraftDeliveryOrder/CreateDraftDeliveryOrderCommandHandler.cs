using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;


namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public class CreateDraftDeliveryOrderCommandHandler : ICommandHandler<CreateDraftDeliveryOrderCommand, Guid>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ITenantContext _tenantContext;

    public CreateDraftDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _tenantContext = tenantContext;
    }

    public async Task<Result<Guid>> Handle(CreateDraftDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var serviceWindow = new ServiceWindow(
            request.ServiceWindow?.Earliest,
            request.ServiceWindow?.Latest);

        var order = Domain.Entities.DeliveryOrder.Create(
            _tenantContext.TenantId,
            request.OrderName,
            request.SlaTier,
            serviceWindow,
            request.StructureType,
            request.Tags);

        foreach (var item in request.OrderItems)
        {
            var dims = item.Dims is null ? null : new Dims(item.Dims.LengthMm, item.Dims.WidthMm, item.Dims.HeightMm);
            var tempRange = item.TemperatureRange is null ? null
                : new TemperatureRange(item.TemperatureRange.MinCelsius, item.TemperatureRange.MaxCelsius);

            order.AddOrderItem(item.PickupLocationCode, item.DropLocationCode,
                item.WorkOrder, item.ItemNumber, item.ItemDescription,
                item.Quantity, item.Weight, item.LoadUnitType,
                item.Line, item.Model, item.Remarks,
                dims, item.HazmatClass, tempRange, item.HandlingInstructions);
        }

        if (request.Schedule != null)
            order.SetRecurringSchedule(
                request.Schedule.CronExpression,
                request.Schedule.ValidFrom,
                request.Schedule.ValidUntil);

        await _repository.AddAsync(order, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(order.Id);
    }
}
