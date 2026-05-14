using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public class BulkSubmitDeliveryOrdersCommandHandler : ICommandHandler<BulkSubmitDeliveryOrdersCommand, List<Guid>>
{
    private readonly IDeliveryOrderRepository _repo;
    private readonly StationValidationService _stationValidation;

    public BulkSubmitDeliveryOrdersCommandHandler(
        IDeliveryOrderRepository repo,
        StationValidationService stationValidation)
    {
        _repo = repo;
        _stationValidation = stationValidation;
    }

    public async Task<Result<List<Guid>>> Handle(BulkSubmitDeliveryOrdersCommand request, CancellationToken cancellationToken)
    {
        if (request.Orders.Count == 0)
            return Result<List<Guid>>.Failure("No orders provided.");

        var orders = new List<Domain.Entities.DeliveryOrder>();

        foreach (var cmd in request.Orders)
        {
            var order = Domain.Entities.DeliveryOrder.Create(
                cmd.OrderRef, cmd.Priority, cmd.CargoType, cmd.RequestedDeliveryDate);

            foreach (var pkg in cmd.Items)
            {
                order.AddItem(
                    pkg.PickupLocationCode, pkg.DropLocationCode,
                    pkg.Sku,
                    pkg.Dimensions is { } d ? Dimensions.Create(d.LengthCm, d.WidthCm, d.HeightCm) : null,
                    pkg.WeightKg,
                    pkg.Quantity.Value,
                    pkg.Quantity.Uom,
                    pkg.CargoSpecific is { } cs
                        ? CargoSpecific.Create(cs.PartNo, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId)
                        : null);
            }

            var stationMap = await _stationValidation.BuildStationMapAsync(order.Items, cancellationToken);
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
}
