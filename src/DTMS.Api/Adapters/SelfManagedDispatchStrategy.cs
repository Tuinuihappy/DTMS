using DTMS.DeliveryOrder.Application.Commands.AssignItemsToTrip;
using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Domain.Services;
using DTMS.SharedKernel.Messaging;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DTMS.Api.Adapters;

// Self-managed dispatch — the source system (OMS/WMS/ERP) executes the
// transport itself and reports lifecycle back. DTMS creates the Trip and
// immediately auto-acknowledges + auto-picks-up it (actor = order.RequestedBy),
// then waits for the source to POST drop + complete via
// /api/v1/source/trips/*. No RIOT3 call, no operator pool, no pool broadcast.
//
// Shape mirrors ManualDispatchStrategy (create → bind items → snapshot) but
// swaps the terminal MarkDispatched(pool) for the AcknowledgeBySource +
// MarkVendorPickedUp pair. OMS notification still fires: MarkVendorStarted
// (via AcknowledgeBySource) emits TripStarted → TripStartedOmsNotifyConsumer,
// and DispatchedAt stays null so that notify is NOT suppressed (parity with
// AMR's TripStarted-notifies behaviour).
//
// Lives in API/Adapters (not a module) for the same reason as
// ManualDispatchStrategy: it bridges Dispatch (ITripRepository) and
// DeliveryOrder (item binding) — cross-module wiring belongs to the
// composition root.
internal sealed class SelfManagedDispatchStrategy : ISelfManagedDispatchService
{
    private readonly ITripRepository _trips;
    private readonly ISender _sender;
    private readonly ITripItemSnapshotProvider _tripItemSnapshotProvider;
    private readonly ILogger<SelfManagedDispatchStrategy> _logger;

    public SelfManagedDispatchStrategy(
        ITripRepository trips,
        ISender sender,
        ITripItemSnapshotProvider tripItemSnapshotProvider,
        ILogger<SelfManagedDispatchStrategy> logger)
    {
        _trips = trips;
        _sender = sender;
        _tripItemSnapshotProvider = tripItemSnapshotProvider;
        _logger = logger;
    }

    public async Task<Result<DispatchGroupOutcome>> DispatchGroupAsync(
        DispatchGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        // RequestedBy is the actor stamped on the auto ack + pickup. It's
        // enforced required at order creation (command + domain guard); this
        // is a defensive backstop so a bad order can't silently self-dispatch
        // with an empty actor.
        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            return Result<DispatchGroupOutcome>.Failure(
                "Self-managed dispatch requires RequestedBy (the auto ack/pickup actor).");
        var actor = request.RequestedBy;

        // Idempotency on UpperKey — MassTransit may redeliver the
        // DeliveryOrderConfirmed event; return the existing trip so we don't
        // create a duplicate or re-fire the ack/pickup events.
        var existing = await _trips.GetByUpperKeyAsync(request.UpperKey, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "[SelfManagedDispatch] ↺ Order {OrderId} group {G} already dispatched (trip {TripId}, upperKey {UpperKey}) — returning existing.",
                request.DeliveryOrderId, request.GroupIndex, existing.Id, request.UpperKey);
            return Result<DispatchGroupOutcome>.Success(new DispatchGroupOutcome(
                TripId: existing.Id, VendorOrderKey: null, TemplateName: "self-managed"));
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

        // Bind items first so the snapshot below is non-empty (rides the
        // TripStarted event to TripItemsProjector). Best-effort — same
        // retry-on-empty contract as the AMR/Manual paths if it faults.
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
                _logger.LogWarning(
                    "[SelfManagedDispatch] Item binding failed for trip {TripId} on order {OrderId}: {Error}",
                    trip.Id, request.DeliveryOrderId, bindResult.Error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[SelfManagedDispatch] Item binding threw for trip {TripId} — snapshot may be empty; OMS notify will retry until items land.",
                trip.Id);
        }

        var itemSnapshots = await _tripItemSnapshotProvider.GetForTripAsync(trip.Id, cancellationToken);

        // Auto acknowledge (Created → InProgress, TripStarted event carries the
        // items) then auto pickup — both attributed to the order's RequestedBy.
        // Both mutate the same tracked aggregate; a single UpdateAsync flushes
        // both domain events through the outbox in order.
        trip.AcknowledgeBySource(actor, items: itemSnapshots);
        trip.MarkVendorPickedUp(actor);
        await _trips.UpdateAsync(trip, cancellationToken);

        _logger.LogInformation(
            "[SelfManagedDispatch] ✓ Order {OrderId} group {G} → trip {TripId} auto ack+pickup by {Actor} " +
            "(items={ItemCount}, upperKey {UpperKey}); awaiting source drop + complete.",
            request.DeliveryOrderId, request.GroupIndex, trip.Id, actor, itemSnapshots.Count, request.UpperKey);

        return Result<DispatchGroupOutcome>.Success(new DispatchGroupOutcome(
            TripId: trip.Id, VendorOrderKey: null, TemplateName: "self-managed"));
    }
}
