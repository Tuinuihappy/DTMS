using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Options;

namespace DTMS.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public class BulkSubmitDeliveryOrdersCommandHandler : ICommandHandler<BulkSubmitDeliveryOrdersCommand, BulkSubmitResult>
{
    private readonly IDeliveryOrderRepository _repo;
    private readonly IStationValidationService _stationValidation;
    private readonly IUomNormalizer _uomNormalizer;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IOrderOriginResolver _originResolver;
    private readonly DeliveryOrderOptions _options;

    public BulkSubmitDeliveryOrdersCommandHandler(
        IDeliveryOrderRepository repo,
        IStationValidationService stationValidation,
        IUomNormalizer uomNormalizer,
        ICurrentUserAccessor currentUser,
        IOrderOriginResolver originResolver,
        IOptions<DeliveryOrderOptions> options)
    {
        _repo = repo;
        _stationValidation = stationValidation;
        _uomNormalizer = uomNormalizer;
        _currentUser = currentUser;
        _originResolver = originResolver;
        _options = options.Value;
    }

    public async Task<Result<BulkSubmitResult>> Handle(BulkSubmitDeliveryOrdersCommand request, CancellationToken cancellationToken)
    {
        if (request.Orders.Count == 0)
            return Result<BulkSubmitResult>.Failure("No orders provided.");

        var succeeded = new List<BulkSubmitSuccess>();
        var failures = new List<BulkSubmitFailure>();

        // Phase P4 — one origin resolve for the whole batch (cheap:
        // cached / claim-first) + one JWT read for RequestedBy.
        var origin = await _originResolver.GetInternalAsync(cancellationToken);
        var actor = _currentUser.GetCurrentUserName();

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
                var serviceWindow = Domain.ValueObjects.ServiceWindow.Create(
                    cmd.ServiceWindow.EarliestUtc, cmd.ServiceWindow.LatestUtc);

                order = Domain.Entities.DeliveryOrder.Create(
                    cmd.OrderRef, cmd.Priority, serviceWindow,
                    origin.Key, origin.DisplayName,
                    actor, actor, cmd.Notes, cmd.RequestedTransportMode);

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
        // IWmsLocationLookup back onto their respective DbContexts which
        // are not safe to use concurrently across orders. WMS PR-2 —
        // dispatch by RequestedTransportMode: AMR resolves station codes;
        // Manual / Fleet resolve WMS location codes (wms.Locations).
        foreach (var order in pendingOrders)
        {
            var mode = order.RequestedTransportMode ?? DTMS.DeliveryOrder.Domain.Enums.TransportMode.Amr;
            IReadOnlyDictionary<string, Guid>? stationMap = null;
            IReadOnlyDictionary<string, Guid>? wmsLocationMap = null;

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
                var wmsResult = await _stationValidation.BuildWmsLocationMapAsync(order.Items, cancellationToken);
                if (wmsResult.IsFailure)
                {
                    failures.Add(new BulkSubmitFailure(order.OrderRef, wmsResult.Error));
                    continue;
                }
                wmsLocationMap = wmsResult.Value;
            }

            // Phase P5 — atomic Submit + Validate + Confirm per order.
            // Mirrors the single-submit handler and the system path so
            // every order that clears validation is durably Confirmed
            // when the bulk call returns.
            order.RaiseCreatedEvent();
            order.Submit();
            order.MarkAsValidated(stationMap, wmsLocationMap);
            order.Confirm(_options.WeightFallbackKg);
            var warnings = WeightWarningEvaluator.Evaluate(order.Items);
            succeeded.Add(new BulkSubmitSuccess(
                DeliveryOrderMapper.MapToDetailDto(order), warnings));
        }

        if (succeeded.Count > 0)
        {
            var succeededIds = new HashSet<Guid>(succeeded.Select(s => s.Order.Id));
            var orders = pendingOrders.Where(o => succeededIds.Contains(o.Id));
            await _repo.AddRangeAsync(orders, cancellationToken);
            await _repo.SaveChangesAsync(cancellationToken);
        }

        return Result<BulkSubmitResult>.Success(new BulkSubmitResult(succeeded, failures));
    }
}
