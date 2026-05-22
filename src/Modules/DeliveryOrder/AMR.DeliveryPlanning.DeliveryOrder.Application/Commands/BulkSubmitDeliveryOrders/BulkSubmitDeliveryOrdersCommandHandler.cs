using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public class BulkSubmitDeliveryOrdersCommandHandler : ICommandHandler<BulkSubmitDeliveryOrdersCommand, BulkSubmitResult>
{
    private readonly IDeliveryOrderRepository _repo;
    private readonly IStationValidationService _stationValidation;
    private readonly IStationLookup _stationLookup;

    public BulkSubmitDeliveryOrdersCommandHandler(
        IDeliveryOrderRepository repo,
        IStationValidationService stationValidation,
        IStationLookup stationLookup)
    {
        _repo = repo;
        _stationValidation = stationValidation;
        _stationLookup = stationLookup;
    }

    public async Task<Result<BulkSubmitResult>> Handle(BulkSubmitDeliveryOrdersCommand request, CancellationToken cancellationToken)
    {
        if (request.Orders.Count == 0)
            return Result<BulkSubmitResult>.Failure("No orders provided.");

        var succeeded = new List<Domain.Entities.DeliveryOrder>();
        var failures = new List<BulkSubmitFailure>();

        // Resolve all location inputs across all orders in one batched lookup so each draft
        // stores the canonical Code form regardless of whether the caller sent a Guid or a Code.
        var normalize = await LocationCodeNormalizer.BuildAsync(
            request.Orders.SelectMany(o => o.Items).SelectMany(i => new[] { i.PickupLocationCode, i.DropLocationCode }),
            _stationLookup, cancellationToken);

        // validate and build orders first (sequential — shares DbContext safely)
        var pendingOrders = new List<Domain.Entities.DeliveryOrder>();
        foreach (var cmd in request.Orders)
        {
            // Bulk always submits, so apply the strict SubmitReadiness rules upfront.
            var validation = SubmitReadiness.Validate(cmd);
            if (!validation.IsValid)
            {
                var errors = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
                failures.Add(new BulkSubmitFailure(cmd.OrderRef, errors));
                continue;
            }

            Domain.Entities.DeliveryOrder order;
            try
            {
                order = Domain.Entities.DeliveryOrder.Create(
                    cmd.OrderRef, cmd.Priority, cmd.RequestedDeliveryDate,
                    cmd.SourceSystem, cmd.CreatedBy);

                foreach (var (pkg, idx) in cmd.Items.Select((p, i) => (p, i + 1)))
                {
                    order.AddItem(
                        normalize(pkg.PickupLocationCode), normalize(pkg.DropLocationCode),
                        idx, pkg.Sku, pkg.Description,
                        pkg.LoadUnitProfileCode,
                        pkg.Dimensions is { } d ? Dimensions.Create(d.LengthMm, d.WidthMm, d.HeightMm) : null,
                        pkg.WeightKg,
                        pkg.Quantity.Value,
                        pkg.Quantity.Uom,
                        pkg.CargoType,
                        pkg.CargoSpecific is { } cs
                            ? CargoSpecific.Create(cs.PartNo, cs.Wo, cs.Line, cs.Vendor, cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                            : null);
                }
            }
            catch (InvalidOperationException ex)
            {
                failures.Add(new BulkSubmitFailure(cmd.OrderRef, ex.Message));
                continue;
            }

            pendingOrders.Add(order);
        }

        // Validate stations sequentially — IStationLookup is backed by a Facility DbContext
        // which is not safe to use concurrently across orders.
        foreach (var order in pendingOrders)
        {
            var stationMap = await _stationValidation.BuildStationMapAsync(order.Items, cancellationToken);
            if (stationMap.IsFailure)
            {
                failures.Add(new BulkSubmitFailure(order.OrderRef, stationMap.Error));
                continue;
            }

            order.Submit();
            order.MarkAsValidated(stationMap.Value);
            succeeded.Add(order);
        }

        if (succeeded.Count > 0)
        {
            await _repo.AddRangeAsync(succeeded, cancellationToken);
            await _repo.SaveChangesAsync(cancellationToken);
        }

        return Result<BulkSubmitResult>.Success(
            new BulkSubmitResult(succeeded.Select(o => o.Id).ToList(), failures));
    }
}
