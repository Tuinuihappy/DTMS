using System.Text.Json;
using DTMS.DeliveryOrder.Application.Commands.AmendDeliveryOrder;
using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TripStatus = DTMS.Dispatch.Domain.Enums.TripStatus;

namespace DTMS.DeliveryOrder.Application.Commands.ReviseAndResendDeliveryOrder;

public class ReviseAndResendDeliveryOrderCommandHandler
    : ICommandHandler<ReviseAndResendDeliveryOrderCommand, DeliveryOrderDetailDto>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAmendmentRepository _amendmentRepo;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly ITripRepository _tripRepository;
    private readonly IStationValidationService _stationValidation;
    private readonly IDispatchStrategyRegistry _strategyRegistry;
    private readonly IRouteDispatchGuard _routeGuard;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly DeliveryOrderOptions _options;
    private readonly ILogger<ReviseAndResendDeliveryOrderCommandHandler> _logger;

    public ReviseAndResendDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAmendmentRepository amendmentRepo,
        IOrderAuditEventRepository auditRepo,
        ITripRepository tripRepository,
        IStationValidationService stationValidation,
        IDispatchStrategyRegistry strategyRegistry,
        IRouteDispatchGuard routeGuard,
        ICurrentUserAccessor currentUser,
        IOptions<DeliveryOrderOptions> options,
        ILogger<ReviseAndResendDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _amendmentRepo = amendmentRepo;
        _auditRepo = auditRepo;
        _tripRepository = tripRepository;
        _stationValidation = stationValidation;
        _strategyRegistry = strategyRegistry;
        _routeGuard = routeGuard;
        _currentUser = currentUser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<DeliveryOrderDetailDto>> Handle(
        ReviseAndResendDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<DeliveryOrderDetailDto>.Failure($"Order {request.OrderId} not found.");

        if (order.Status != OrderStatus.Failed)
            return Result<DeliveryOrderDetailDto>.Failure(
                $"Only Failed orders can be revised and resent. Current status: {order.Status}.");

        // Same race guard as Reopen / Redispatch — a still-running trip would
        // end up dispatched alongside the new attempt. A Failed order has only
        // terminal trips by construction, so this normally passes.
        var trips = await _tripRepository.GetByDeliveryOrderIdAsync(order.Id, cancellationToken);
        var activeTrips = trips.Count(t =>
            t.Status is TripStatus.Created or TripStatus.InProgress or TripStatus.Hang or TripStatus.Held);
        if (activeTrips > 0)
            return Result<DeliveryOrderDetailDto>.Failure(
                $"Order still has {activeTrips} active trip(s). Wait for them to settle (or cancel them), then resend.");

        // Same gate as the create paths — an unregistered mode would fail
        // every item in Planning and land the order straight back at Failed.
        var mode = order.RequestedTransportMode ?? TransportMode.Amr;
        if (!_strategyRegistry.IsRegistered(mode))
            throw new TransportModeNotEnabledException(mode);

        var revisedBy = _currentUser.GetCurrentUserName() ?? "system";
        var originalSnapshot = JsonSerializer.Serialize(OrderSnapshotV1.From(order));

        try
        {
            var serviceWindow = ServiceWindow.Create(
                request.ServiceWindow.EarliestUtc, request.ServiceWindow.LatestUtc);

            var revisions = request.Items.Select(i => new ItemRevision(
                i.Id,
                i.PickupLocationCode, i.DropLocationCode,
                i.ItemId.Trim(), i.Description, i.LoadUnitProfileCode,
                i.Dimensions is { } d ? Dimensions.Create(d.LengthMm, d.WidthMm, d.HeightMm) : null,
                i.WeightKg,
                Quantity.Create(i.Quantity.Value, i.Quantity.Uom),
                i.Hazmat is { } hz ? HazmatInfo.Create(hz.ClassCode, hz.PackingGroup) : null,
                i.Temperature is { } tr ? TemperatureRange.Create(tr.MinC, tr.MaxC) : null,
                i.HandlingInstructions)).ToList();

            var outcome = order.ReviseFailed(request.Priority, serviceWindow, revisions);

            // Re-resolve every code — the operator may have pointed an item at
            // a different station. Same mode split as the upstream intake. A
            // failure returns before SaveChanges, so nothing is persisted.
            IReadOnlyDictionary<string, Guid>? stationMap = null;
            IReadOnlyDictionary<string, Guid>? wmsLocationMap = null;
            if (mode == TransportMode.Amr)
            {
                var stationResult = await _stationValidation.BuildStationMapAsync(order.Items, cancellationToken);
                if (stationResult.IsFailure)
                    return Result<DeliveryOrderDetailDto>.Failure(stationResult.Error);
                stationMap = stationResult.Value;
                // Refuse a revision onto a route nothing can carry out — 422,
                // before anything is saved, instead of failing again in Planning.
                await _routeGuard.EnsureDispatchableAsync(order.OrderRef, order.Items, mode, stationMap, cancellationToken);
            }
            else
            {
                var wmsResult = await _stationValidation.BuildWmsLocationMapAsync(order.Items, cancellationToken);
                if (wmsResult.IsFailure)
                    return Result<DeliveryOrderDetailDto>.Failure(wmsResult.Error);
                wmsLocationMap = wmsResult.Value;
            }

            var reason = $"Revised & resent by {revisedBy}: {request.Reason.Trim()}";
            order.ResendAfterRevision(stationMap, wmsLocationMap, _options.WeightFallbackKg, reason);

            await _repository.RemoveItemsAsync(outcome.Removed, cancellationToken);
            await _repository.AddItemsAsync(outcome.Added, cancellationToken);

            await _amendmentRepo.AddAsync(new OrderAmendment(
                order.Id, AmendmentType.CombinedChange, request.Reason.Trim(),
                originalSnapshot, JsonSerializer.Serialize(OrderSnapshotV1.From(order)), revisedBy,
                amendmentVersion: 1), cancellationToken);
            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderRevisedAndResent",
                $"Order '{order.OrderRef}' {reason} ({order.Items.Count} item(s), " +
                $"{outcome.Added.Count} added, {outcome.Removed.Count} removed)"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[ReviseAndResend] Order {OrderId} '{OrderRef}' revised by {By} ({Items} items, +{Added} -{Removed}) and sent back to Planning: {Reason}",
                order.Id, order.OrderRef, revisedBy, order.Items.Count,
                outcome.Added.Count, outcome.Removed.Count, request.Reason);

            return Result<DeliveryOrderDetailDto>.Success(DeliveryOrderMapper.MapToDetailDto(order));
        }
        // RouteNotDispatchableException is deliberately not caught here — it
        // propagates to the middleware as 422, before anything is saved.
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning("[ReviseAndResend] Order {OrderId} rejected: {Error}", request.OrderId, ex.Message);
            return Result<DeliveryOrderDetailDto>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[ReviseAndResend] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result<DeliveryOrderDetailDto>.Failure("The order was modified by another process. Please retry.");
        }
    }
}
