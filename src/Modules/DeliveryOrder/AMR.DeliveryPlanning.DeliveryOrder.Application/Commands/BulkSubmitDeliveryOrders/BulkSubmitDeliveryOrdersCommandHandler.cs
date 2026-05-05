using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
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
            var serviceWindow = new ServiceWindow(
                cmd.ServiceWindow?.Earliest,
                cmd.ServiceWindow?.Latest);

            var order = Domain.Entities.DeliveryOrder.Create(
                _tenantContext.TenantId, cmd.OrderName,
                cmd.SlaTier, serviceWindow, cmd.StructureType, cmd.Tags);

            foreach (var item in cmd.OrderItems)
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

            if (cmd.Schedule != null)
                order.SetRecurringSchedule(cmd.Schedule.CronExpression, cmd.Schedule.ValidFrom, cmd.Schedule.ValidUntil);

            var stationMap = await BuildStationMapAsync(order.Legs, cancellationToken);
            if (stationMap.IsFailure) return Result<List<Guid>>.Failure(stationMap.Error);

            order.Submit();
            order.MarkAsValidated(stationMap.Value);
            order.MarkReadyToPlan();

            orders.Add(order);
        }

        await _repo.AddRangeAsync(orders, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result<List<Guid>>.Success(orders.Select(o => o.Id).ToList());
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
