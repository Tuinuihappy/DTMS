using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;

public class CreateUpstreamDeliveryOrderCommandHandler : ICommandHandler<CreateUpstreamDeliveryOrderCommand, UpstreamOrderAckDto>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly IStationValidationService _stationValidation;
    private readonly IUomNormalizer _uomNormalizer;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IOrderOriginResolver _originResolver;
    private readonly DeliveryOrderOptions _options;
    private readonly ILogger<CreateUpstreamDeliveryOrderCommandHandler> _logger;

    public CreateUpstreamDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        IStationValidationService stationValidation,
        IUomNormalizer uomNormalizer,
        ICurrentUserAccessor currentUser,
        IOrderOriginResolver originResolver,
        IOptions<DeliveryOrderOptions> options,
        ILogger<CreateUpstreamDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _stationValidation = stationValidation;
        _uomNormalizer = uomNormalizer;
        _currentUser = currentUser;
        _originResolver = originResolver;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<UpstreamOrderAckDto>> Handle(CreateUpstreamDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        // Phase P4 — resolve the origin snapshot before the idempotency
        // lookup. Middleware has already vetted the URL {key} is active,
        // but a race with admin delete could return null here — treat as
        // a hard failure. Handler-side check is defense in depth; the
        // usual outcome is a claim-first hit that skips the DB.
        var origin = await _originResolver.GetByKeyAsync(request.SourceSystemKey, cancellationToken);
        if (origin is null)
        {
            _logger.LogWarning("[Upstream] Unknown source key '{Key}' at handler entry (raced with admin delete?).",
                request.SourceSystemKey);
            return Result<UpstreamOrderAckDto>.Failure(
                $"Unknown source system '{request.SourceSystemKey}'.");
        }

        // Self-managed is Manual-only (it replaces the operator pool, not AMR's
        // RIOT3 lifecycle) and auto-acks + auto-picks-up at trip creation using
        // RequestedBy as the actor. Reject up front with clear 400s; the domain
        // guards both invariants defensively too.
        if (request.SelfManaged && request.RequestedTransportMode != Domain.Enums.TransportMode.Manual)
            return Result<UpstreamOrderAckDto>.Failure(
                "selfManaged is only supported for Manual transport mode (requestedTransportMode=Manual).");
        if (request.SelfManaged && string.IsNullOrWhiteSpace(request.RequestedBy))
            return Result<UpstreamOrderAckDto>.Failure(
                "requestedBy is required when selfManaged is true — it is the actor recorded on the auto acknowledge + pickup.");

        // Idempotency check — if (SourceSystemKey, OrderRef) already exists, return existing ack
        var existing = await _repository.GetByRefAsync(request.SourceSystemKey, request.OrderRef, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("[Upstream] Order '{OrderRef}' from {SourceSystemKey} already exists with id {OrderId} — returning existing.",
                request.OrderRef, request.SourceSystemKey, existing.Id);
            // GetByRefAsync doesn't include Items — refetch the full graph for the DetailDto.
            var full = await _repository.GetByIdAsNoTrackingAsync(existing.Id, cancellationToken);
            return Result<UpstreamOrderAckDto>.Success(
                new UpstreamOrderAckDto(DeliveryOrderMapper.MapToDetailDto(full!), Array.Empty<OrderQualityIssue>()));
        }

        Domain.Entities.DeliveryOrder order;
        try
        {
            var serviceWindow = Domain.ValueObjects.ServiceWindow.Create(
                request.ServiceWindow.EarliestUtc, request.ServiceWindow.LatestUtc);

            order = Domain.Entities.DeliveryOrder.CreateFromUpstream(
                request.OrderRef, request.Priority, serviceWindow,
                origin.Key, origin.DisplayName,
                origin.DisplayName,
                request.RequestedBy, request.Notes,
                request.RequestedTransportMode,
                request.SelfManaged);

            foreach (var (item, idx) in request.Items.Select((p, i) => (p, i + 1)))
            {
                var uom = _uomNormalizer.Normalize(item.Quantity.Uom);
                if (uom is null)
                    return Result<UpstreamOrderAckDto>.Failure(
                        $"Unknown UOM '{item.Quantity.Uom}' on item {idx} — accepted: KG, G, LB, EA, BOX, PALLET, CASE (or configured aliases).");

                order.AddItem(
                    item.PickupLocationCode, item.DropLocationCode,
                    idx, item.ItemId, item.Description,
                    item.LoadUnitProfileCode,
                    item.Dimensions is { } d ? Dimensions.Create(d.LengthMm, d.WidthMm, d.HeightMm) : null,
                    item.WeightKg,
                    Quantity.Create(item.Quantity.Value, uom.Value),
                    item.Hazmat is { } hz
                        ? HazmatInfo.Create(hz.ClassCode, hz.PackingGroup)
                        : null,
                    item.Temperature is { } tr
                        ? TemperatureRange.Create(tr.MinC, tr.MaxC)
                        : null,
                    item.HandlingInstructions);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Upstream] Order '{OrderRef}' build failed: {Error}.", request.OrderRef, ex.Message);
            return Result<UpstreamOrderAckDto>.Failure(ex.Message);
        }

        // WMS PR-2 — interpret location codes by mode. Upstream OMS still
        // treats AMR as the default when RequestedTransportMode isn't
        // supplied; Manual / Fleet upstream payloads (when those modes go
        // live) supply the mode and the same PickupLocationCode field is
        // interpreted as a WMS location code (wms.Locations).
        var mode = order.RequestedTransportMode ?? DTMS.DeliveryOrder.Domain.Enums.TransportMode.Amr;
        IReadOnlyDictionary<string, Guid>? stationMap = null;
        IReadOnlyDictionary<string, Guid>? wmsLocationMap = null;

        if (mode == DTMS.DeliveryOrder.Domain.Enums.TransportMode.Amr)
        {
            var stationResult = await _stationValidation.BuildStationMapAsync(order.Items, cancellationToken);
            if (stationResult.IsFailure)
            {
                _logger.LogWarning("[Upstream] Order '{OrderRef}' station mapping failed: {Error}.", request.OrderRef, stationResult.Error);
                return Result<UpstreamOrderAckDto>.Failure(stationResult.Error);
            }
            stationMap = stationResult.Value;
        }
        else
        {
            var wmsResult = await _stationValidation.BuildWmsLocationMapAsync(order.Items, cancellationToken);
            if (wmsResult.IsFailure)
            {
                _logger.LogWarning("[Upstream] Order '{OrderRef}' WMS location mapping failed: {Error}.", request.OrderRef, wmsResult.Error);
                return Result<UpstreamOrderAckDto>.Failure(wmsResult.Error);
            }
            wmsLocationMap = wmsResult.Value;
        }

        try
        {
            order.RaiseCreatedEvent();
            order.MarkAsValidated(stationMap, wmsLocationMap);
            order.Confirm(_options.WeightFallbackKg);
            // POD-required orders sit at DroppedOff until operator scans;
            // upstream caller opts in via the optional flag. Null leaves
            // it for the order-level / template-level default to decide.
            if (request.RequiresDropPod.HasValue)
                order.SetRequiresDropPod(request.RequiresDropPod.Value);
            if (request.RequiresPickupPod.HasValue)
                order.SetRequiresPickupPod(request.RequiresPickupPod.Value);

            await _repository.AddAsync(order, cancellationToken);
            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderUpstreamIngested",
                $"Order '{order.OrderRef}' ingested from {order.SourceSystemKey} by {order.CreatedBy ?? "system"} — auto-confirmed and queued for planning"), cancellationToken);

            var warnings = WeightWarningEvaluator.Evaluate(order.Items);
            foreach (var w in warnings)
                await _auditRepo.AddAsync(new OrderAuditEvent(order.Id, "QualityWarning", $"{w.Code}: {w.Message}"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Upstream] Order {OrderId} '{OrderRef}' from {SourceSystemKey} auto-pipelined to Confirmed ({WarningCount} warning(s)).",
                order.Id, order.OrderRef, order.SourceSystemKey, warnings.Count);

            return Result<UpstreamOrderAckDto>.Success(
                new UpstreamOrderAckDto(DeliveryOrderMapper.MapToDetailDto(order), warnings));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Upstream] Order '{OrderRef}' pipeline failed: {Error}.", request.OrderRef, ex.Message);
            return Result<UpstreamOrderAckDto>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Upstream] Concurrency conflict on Order '{OrderRef}'.", request.OrderRef);
            return Result<UpstreamOrderAckDto>.Failure("The order was modified by another process. Please retry.");
        }
        catch (DbUpdateException)
        {
            // Likely unique-index violation from a concurrent insert of the same (SourceSystemKey, OrderRef).
            // Re-query: if the row now exists, treat as idempotent success; otherwise surface the original error.
            var raced = await _repository.GetByRefAsync(request.SourceSystemKey, request.OrderRef, cancellationToken);
            if (raced is null) throw;

            _logger.LogInformation("[Upstream] Order '{OrderRef}' raced with concurrent insert — returning existing id {OrderId}.",
                request.OrderRef, raced.Id);
            var racedFull = await _repository.GetByIdAsNoTrackingAsync(raced.Id, cancellationToken);
            return Result<UpstreamOrderAckDto>.Success(
                new UpstreamOrderAckDto(DeliveryOrderMapper.MapToDetailDto(racedFull!), Array.Empty<OrderQualityIssue>()));
        }
    }
}
