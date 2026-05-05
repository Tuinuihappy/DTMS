using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

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
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<Guid>.Failure($"Order {request.OrderId} not found.");

        var stationMap = await BuildStationMapAsync(order.Legs, cancellationToken);
        if (stationMap.IsFailure) return Result<Guid>.Failure(stationMap.Error);

        try
        {
            order.Submit();
            order.MarkAsValidated(stationMap.Value);
            order.MarkReadyToPlan();

            await _repository.UpdateAsync(order, cancellationToken);

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderSubmitted",
                $"Order '{order.OrderName}' submitted with SLA tier {order.SlaTier}",
                _tenantContext.TenantId.ToString()), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(order.Id);
        }
        catch (InvalidOperationException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<Guid>.Failure("The order was modified by another process. Please retry.");
        }
    }

    private async Task<Result<IReadOnlyDictionary<(string pickup, string drop), (Guid pickupStationId, Guid dropStationId)>>>
        BuildStationMapAsync(IEnumerable<Domain.Entities.DeliveryLeg> legs, CancellationToken cancellationToken)
    {
        var uniquePairs = legs
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
