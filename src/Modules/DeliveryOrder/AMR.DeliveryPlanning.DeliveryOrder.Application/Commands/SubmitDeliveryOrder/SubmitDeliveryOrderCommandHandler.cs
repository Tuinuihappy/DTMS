using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public class SubmitDeliveryOrderCommandHandler : ICommandHandler<SubmitDeliveryOrderCommand, Guid>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly StationValidationService _stationValidation;
    private readonly ITenantContext _tenantContext;

    public SubmitDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        StationValidationService stationValidation,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _stationValidation = stationValidation;
        _tenantContext = tenantContext;
    }

    public async Task<Result<Guid>> Handle(SubmitDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var stationMap = await BuildStationMapAsync(request.Lines, cancellationToken);
        if (stationMap.IsFailure) return Result<Guid>.Failure(stationMap.Error);

        var order = new Domain.Entities.DeliveryOrder(
            _tenantContext.TenantId,
            request.OrderKey,
            request.Priority,
            request.SLA);

        foreach (var line in request.Lines)
            order.AddOrderLine(line.PickupLocationCode, line.DropLocationCode,
                line.WorkOrderId, line.WorkOrder, line.ItemId, line.ItemNumber,
                line.ItemDescription, line.Quantity, line.Weight, line.Remarks);

        if (request.Schedule != null)
            order.SetRecurringSchedule(request.Schedule.CronExpression, request.Schedule.ValidFrom, request.Schedule.ValidUntil);

        order.MarkAsValidated(stationMap.Value);
        order.MarkReadyToPlan();

        await _repository.AddAsync(order, cancellationToken);

        await _auditRepo.AddAsync(new OrderAuditEvent(
            order.Id, "OrderSubmitted",
            $"Order {request.OrderKey} submitted with priority {request.Priority}",
            _tenantContext.TenantId.ToString()), cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(order.Id);
    }

    private async Task<Result<IReadOnlyDictionary<(string pickup, string drop), (Guid pickupStationId, Guid dropStationId)>>>
        BuildStationMapAsync(IEnumerable<OrderLineDto> lines, CancellationToken cancellationToken)
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
