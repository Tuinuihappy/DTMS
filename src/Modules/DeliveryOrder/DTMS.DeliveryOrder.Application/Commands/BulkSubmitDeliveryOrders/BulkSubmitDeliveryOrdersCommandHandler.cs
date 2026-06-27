using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public class BulkSubmitDeliveryOrdersCommandHandler : ICommandHandler<BulkSubmitDeliveryOrdersCommand, BulkSubmitResult>
{
    private readonly IDeliveryOrderRepository _repo;
    private readonly IStationValidationService _stationValidation;
    private readonly IUomNormalizer _uomNormalizer;
    private readonly ICurrentUserAccessor _currentUser;

    public BulkSubmitDeliveryOrdersCommandHandler(
        IDeliveryOrderRepository repo,
        IStationValidationService stationValidation,
        IUomNormalizer uomNormalizer,
        ICurrentUserAccessor currentUser)
    {
        _repo = repo;
        _stationValidation = stationValidation;
        _uomNormalizer = uomNormalizer;
        _currentUser = currentUser;
    }

    public async Task<Result<BulkSubmitResult>> Handle(BulkSubmitDeliveryOrdersCommand request, CancellationToken cancellationToken)
    {
        if (request.Orders.Count == 0)
            return Result<BulkSubmitResult>.Failure("No orders provided.");

        var succeeded = new List<BulkSubmitSuccess>();
        var failures = new List<BulkSubmitFailure>();

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
                var serviceWindow = cmd.ServiceWindow is { } sw
                    ? Domain.ValueObjects.ServiceWindow.Create(sw.EarliestUtc, sw.LatestUtc)
                    : null;

                order = Domain.Entities.DeliveryOrder.Create(
                    cmd.OrderRef, cmd.Priority, serviceWindow,
                    Domain.Enums.SourceSystem.Manual, _currentUser.GetCurrentUserName(),
                    cmd.RequestedBy, cmd.Notes, cmd.RequestedTransportMode);

                var uomFailureForOrder = false;
                foreach (var (pkg, idx) in cmd.Items.Select((p, i) => (p, i + 1)))
                {
                    var uom = _uomNormalizer.Normalize(pkg.Quantity.Uom);
                    if (uom is null)
                    {
                        failures.Add(new BulkSubmitFailure(cmd.OrderRef,
                            $"Unknown UOM '{pkg.Quantity.Uom}' on item {idx}."));
                        uomFailureForOrder = true;
                        break;
                    }

                    order.AddItem(
                        pkg.PickupLocationCode, pkg.DropLocationCode,
                        idx, pkg.ItemId, pkg.Description,
                        pkg.LoadUnitProfileCode,
                        pkg.Dimensions is { } d ? Dimensions.Create(d.LengthMm, d.WidthMm, d.HeightMm) : null,
                        pkg.WeightKg,
                        Quantity.Create(pkg.Quantity.Value, uom.Value),
                        pkg.Hazmat is { } hz
                            ? HazmatInfo.Create(hz.ClassCode, hz.PackingGroup)
                            : null,
                        pkg.Temperature is { } tr
                            ? TemperatureRange.Create(tr.MinC, tr.MaxC)
                            : null,
                        pkg.HandlingInstructions);
                }

                if (uomFailureForOrder) continue;
            }
            catch (InvalidOperationException ex)
            {
                failures.Add(new BulkSubmitFailure(cmd.OrderRef, ex.Message));
                continue;
            }

            pendingOrders.Add(order);
        }

        // Validate locations sequentially — both IStationLookup and
        // IWarehouseLookup back onto the Facility DbContext which is
        // not safe to use concurrently across orders. Per Phase 2.5
        // Path A, dispatch by RequestedTransportMode: AMR resolves
        // station codes; Manual / Fleet resolve warehouse codes.
        foreach (var order in pendingOrders)
        {
            var mode = order.RequestedTransportMode ?? DTMS.DeliveryOrder.Domain.Enums.TransportMode.Amr;
            IReadOnlyDictionary<string, Guid>? stationMap = null;
            IReadOnlyDictionary<string, Guid>? warehouseMap = null;

            if (mode == DTMS.DeliveryOrder.Domain.Enums.TransportMode.Amr)
            {
                var stationResult = await _stationValidation.BuildStationMapAsync(order.Items, cancellationToken);
                if (stationResult.IsFailure)
                {
                    failures.Add(new BulkSubmitFailure(order.OrderRef, stationResult.Error));
                    continue;
                }
                stationMap = stationResult.Value;
            }
            else
            {
                var warehouseResult = await _stationValidation.BuildWarehouseMapAsync(order.Items, cancellationToken);
                if (warehouseResult.IsFailure)
                {
                    failures.Add(new BulkSubmitFailure(order.OrderRef, warehouseResult.Error));
                    continue;
                }
                warehouseMap = warehouseResult.Value;
            }

            order.RaiseCreatedEvent();
            order.Submit();
            order.MarkAsValidated(stationMap, warehouseMap);
            var warnings = WeightWarningEvaluator.Evaluate(order.Items);
            succeeded.Add(new BulkSubmitSuccess(order.Id, warnings));
        }

        if (succeeded.Count > 0)
        {
            var orders = pendingOrders.Where(o => succeeded.Any(s => s.OrderId == o.Id));
            await _repo.AddRangeAsync(orders, cancellationToken);
            await _repo.SaveChangesAsync(cancellationToken);
        }

        return Result<BulkSubmitResult>.Success(new BulkSubmitResult(succeeded, failures));
    }
}
