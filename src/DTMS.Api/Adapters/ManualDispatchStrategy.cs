using DTMS.DeliveryOrder.Application.Commands.AssignItemsToTrip;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Domain.Services;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Application.Queries.GetPoolTrips;
using DTMS.Transport.Manual.Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.Api.Adapters;

// WMS PR-4b — Manual/Fleet pool dispatch strategy (PR-E deleted the legacy
// auto-assign branch after PoolMode ran stable in prod).
//
// Flow:
//   1. Idempotency check on UpperKey — MassTransit redelivery would
//      otherwise create a duplicate Trip.
//   2. Create Trip in Created state (Manual mode has no external vendor
//      key so no AmrTripExtension is materialized).
//   3. Bind items via AssignItemsToTripCommand — committed on the
//      DeliveryOrder DbContext so the snapshot query below sees them.
//   4. Snapshot items via ITripItemSnapshotProvider for the outbound
//      event so TripItemsProjector materializes dispatch.TripItems at
//      dispatch time (the operator pool card renders from it).
//   5. Trip.MarkDispatched(items) → stamps DispatchedAt + fires
//      TripDispatchedDomainEvent (Status stays Created — parity with AMR).
//   6. UpdateAsync flushes → outbox emits TripDispatchedIntegrationEventV1
//      → TripStartedOmsNotifyConsumer notifies OMS with DeliveryBy=null.
//   7. Fire-and-forget SignalR broadcast so every connected operator PWA
//      inserts the card without waiting for their next REST refresh.
//
// Operator selection, ManualTripExtension, SLA windows, and push
// notification are NOT done here — they move to AcknowledgeTripCommandHandler
// (the pool claim path) so a Trip lands in the pool without binding to
// any operator.
//
// Why this lives in API/Adapters (not Transport.Manual.Application):
//   It bridges three modules — Dispatch (ITripRepository), Transport.Manual
//   (IOperatorPoolBroadcaster), and DeliveryOrder (item binding). Module
//   boundaries say each module's Application/Domain stays pure; the
//   composition root owns the cross-module wiring. Same shape as
//   AmrDispatchStrategy.
internal sealed class ManualDispatchStrategy : IDispatchStrategy
{
    private readonly ITripRepository _trips;
    private readonly ISender _sender;
    private readonly ITripItemSnapshotProvider _tripItemSnapshotProvider;
    private readonly IOperatorPoolBroadcaster _poolBroadcaster;
    private readonly ManualDispatchOptions _options;
    private readonly ILogger<ManualDispatchStrategy> _logger;

    public ManualDispatchStrategy(
        ITripRepository trips,
        ISender sender,
        ITripItemSnapshotProvider tripItemSnapshotProvider,
        IOperatorPoolBroadcaster poolBroadcaster,
        IOptions<ManualDispatchOptions> options,
        ILogger<ManualDispatchStrategy> logger)
    {
        _trips = trips;
        _sender = sender;
        _tripItemSnapshotProvider = tripItemSnapshotProvider;
        _poolBroadcaster = poolBroadcaster;
        _options = options.Value;
        _logger = logger;
    }

    public TransportMode Mode => TransportMode.Manual;

    public IReadOnlyList<DispatchGroup> GroupItems(IReadOnlyList<DispatchGroupItem> items)
        => items
            .Where(i => i.PickupWmsLocationId.HasValue && i.DropWmsLocationId.HasValue)
            .GroupBy(i => (Pickup: i.PickupWmsLocationId!.Value, Drop: i.DropWmsLocationId!.Value))
            .Select(g => new DispatchGroup(
                PickupStationId:     null,
                DropStationId:       null,
                Items:               g.ToList(),
                PickupWmsLocationId: g.Key.Pickup,
                DropWmsLocationId:   g.Key.Drop))
            .ToList();

    public async Task<Result<DispatchGroupOutcome>> DispatchGroupAsync(
        DispatchGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableDispatch)
        {
            return Result<DispatchGroupOutcome>.Failure(
                "Manual dispatch is disabled (TransportModes:Manual:Dispatch:EnableDispatch=false).");
        }

        // Idempotency check on UpperKey. MassTransit may redeliver the
        // DeliveryOrderConfirmed event (graceful shutdown, slow ack, etc.);
        // returning the existing trip prevents a duplicate row + duplicate
        // TripDispatched broadcast.
        var existing = await _trips.GetByUpperKeyAsync(request.UpperKey, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "[ManualDispatch] ↺ Order {OrderId} group {G} already dispatched (trip {TripId}, upperKey {UpperKey}) — returning existing.",
                request.DeliveryOrderId, request.GroupIndex, existing.Id, request.UpperKey);
            return Result<DispatchGroupOutcome>.Success(new DispatchGroupOutcome(
                TripId: existing.Id,
                VendorOrderKey: null,
                TemplateName: "manual-pool"));
        }

        var trip = Trip.CreateForEnvelope(
            deliveryOrderId: request.DeliveryOrderId,
            upperKey: request.UpperKey,
            vendorOrderKey: null,
            pickupStationId: request.PickupStationId == Guid.Empty ? null : request.PickupStationId,
            dropStationId: request.DropStationId == Guid.Empty ? null : request.DropStationId,
            attemptNumber: request.AttemptNumber,
            previousAttemptId: request.PreviousAttemptId,
            templateNameAtDispatch: null,
            priorityAtDispatch: request.PriorityOverride,
            vendorRequestSnapshot: null,
            jobId: request.JobId,
            pickupWmsLocationId: request.PickupWmsLocationId,
            dropWmsLocationId: request.DropWmsLocationId);
        await _trips.AddAsync(trip, cancellationToken);

        // Bind items first so the snapshot below is non-empty and the OMS
        // consumer's item lookup finds bound items on first delivery. If
        // this fails the trip still transitions to Dispatched — the OMS
        // consumer's retry-on-empty-items handles the eventual-consistency
        // gap (same contract used by AMR trip-started flow).
        try
        {
            var bindResult = await _sender.Send(new AssignItemsToTripCommand(
                OrderId: request.DeliveryOrderId,
                TripId: trip.Id,
                AttemptNumber: request.AttemptNumber,
                PickupStationId: request.PickupStationId,
                DropStationId: request.DropStationId,
                PickupWmsLocationId: request.PickupWmsLocationId,
                DropWmsLocationId: request.DropWmsLocationId), cancellationToken);
            if (bindResult.IsFailure)
            {
                _logger.LogWarning(
                    "[ManualDispatch] Item binding failed for trip {TripId} on order {OrderId}: {Error}",
                    trip.Id, request.DeliveryOrderId, bindResult.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ManualDispatch] Item binding threw for trip {TripId} — snapshot may be empty; OMS notify will retry until items land.",
                trip.Id);
        }

        var itemSnapshots = await _tripItemSnapshotProvider.GetForTripAsync(trip.Id, cancellationToken);
        trip.MarkDispatched(items: itemSnapshots);
        await _trips.UpdateAsync(trip, cancellationToken);

        _logger.LogInformation(
            "[ManualDispatch] ✓ Order {OrderId} group {G} → trip {TripId} DISPATCHED to pool (items={ItemCount}, upperKey {UpperKey})",
            request.DeliveryOrderId, request.GroupIndex, trip.Id, itemSnapshots.Count, request.UpperKey);

        // Realtime hint for connected operators. Fire-and-forget: broadcaster
        // swallows exceptions so a hub hiccup doesn't roll back the DB
        // commit above. Every connected PWA in the "operator-pool" group
        // inserts the card without waiting for its next REST refresh.
        var pickupItem = itemSnapshots.FirstOrDefault();
        var addedDto = new PoolTripDto(
            TripId:          trip.Id,
            DeliveryOrderId: trip.DeliveryOrderId,
            OrderRef:        pickupItem?.OrderRef ?? string.Empty,
            PickupCode:      pickupItem?.PickupCode ?? string.Empty,
            DropCode:        pickupItem?.DropCode ?? string.Empty,
            ItemCount:       itemSnapshots.Count,
            TotalWeightKg:   itemSnapshots.Sum(i => i.WeightKg ?? 0.0),
            DispatchedAt:    trip.DispatchedAt!.Value,
            Priority:        trip.PriorityAtDispatch);
        await _poolBroadcaster.BroadcastAddedAsync(addedDto, cancellationToken);

        return Result<DispatchGroupOutcome>.Success(new DispatchGroupOutcome(
            TripId: trip.Id,
            VendorOrderKey: null,
            TemplateName: "manual-pool"));
    }
}
