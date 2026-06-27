using DTMS.DeliveryOrder.Application.Commands.AssignItemsToTrip;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Enums;
using DTMS.Transport.Manual.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.Api.Adapters;

// Phase 4.4 — Real ManualDispatchStrategy (replaces the Phase 3c stub).
//
// Flow:
//   1. Resolve eligible operator via IOperatorAssignmentPolicy
//      (warehouse-scoped, cert-aware in 4.4 although the consumer
//      doesn't supply cargo cert yet).
//   2. Persist Trip with no vendor key (Manual mode has no external
//      vendor — the operator IS the executor).
//   3. Persist ManualTripExtension binding Trip → Operator + SLA windows.
//   4. Mutate Operator.AssignToTrip — fires OperatorAssignedToTrip
//      domain event for projections.
//   5. Fire push notification (best-effort — log if it fails; trip
//      still proceeds because the operator can also find the trip on
//      their PWA's polled /trips/assigned).
//
// Failure modes:
//   - No eligible operator       → Result.Failure → consumer marks Job/Items Failed
//   - Trip persistence failure   → throws; consumer's catch marks Job Failed
//   - Push notification failure  → swallowed + logged (best-effort)
//
// Why this lives in API/Adapters (not Transport.Manual.Application):
//   It bridges three modules — Dispatch (ITripRepository), Transport.Manual
//   (Operator, ManualTripExtension), and the push gateway. Module
//   boundaries say each module's Application/Domain stays pure; the
//   composition root owns the cross-module wiring. Same shape as
//   AmrDispatchStrategy.
internal sealed class ManualDispatchStrategy : IDispatchStrategy
{
    private readonly IOperatorAssignmentPolicy _policy;
    private readonly ITripRepository _trips;
    private readonly IManualTripExtensionRepository _extensions;
    private readonly IOperatorRepository _operators;
    private readonly IPushNotificationGateway _push;
    private readonly ISender _sender;
    private readonly ManualDispatchOptions _options;
    private readonly ILogger<ManualDispatchStrategy> _logger;

    public ManualDispatchStrategy(
        IOperatorAssignmentPolicy policy,
        ITripRepository trips,
        IManualTripExtensionRepository extensions,
        IOperatorRepository operators,
        IPushNotificationGateway push,
        ISender sender,
        IOptions<ManualDispatchOptions> options,
        ILogger<ManualDispatchStrategy> logger)
    {
        _policy = policy;
        _trips = trips;
        _extensions = extensions;
        _operators = operators;
        _push = push;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    public TransportMode Mode => TransportMode.Manual;

    public IReadOnlyList<DispatchGroup> GroupItems(IReadOnlyList<DispatchGroupItem> items)
        => items
            .Where(i => i.PickupWarehouseId.HasValue && i.DropWarehouseId.HasValue)
            .GroupBy(i => (Pickup: i.PickupWarehouseId!.Value, Drop: i.DropWarehouseId!.Value))
            .Select(g => new DispatchGroup(
                PickupStationId:   null,
                DropStationId:     null,
                PickupWarehouseId: g.Key.Pickup,
                DropWarehouseId:   g.Key.Drop,
                Items: g.ToList()))
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

        // ── 0. Idempotency check on UpperKey ─────────────────────────
        // MassTransit may redeliver the DeliveryOrderConfirmed event
        // (graceful shutdown, slow ack, etc.) — the second pass through
        // this strategy must NOT create a duplicate Trip + try to assign
        // an operator who's already bound to the existing trip (which
        // would throw and mark the order Failed even though the first
        // dispatch succeeded).
        //
        // AMR handles this via DispatchByRouteAsync's vendor-key
        // persistence; Manual has no vendor so we look up Trip by
        // UpperKey directly. Same idempotency contract.
        var existing = await _trips.GetByUpperKeyAsync(request.UpperKey, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "[ManualDispatch] ↺ Order {OrderId} group {G} already dispatched (trip {TripId}, upperKey {UpperKey}) — returning existing.",
                request.DeliveryOrderId, request.GroupIndex, existing.Id, request.UpperKey);
            return Result<DispatchGroupOutcome>.Success(new DispatchGroupOutcome(
                TripId: existing.Id,
                VendorOrderKey: null,
                TemplateName: "manual"));
        }

        // ── 1. Pick an operator ──────────────────────────────────────
        // Cert filter is empty in 4.4 — cargo-cert plumbing comes in a
        // later phase once ItemEventDto carries hazmat / cold-chain flags.
        var selection = await _policy.SelectOperatorAsync(
            new OperatorAssignmentContext(
                PickupWarehouseId: request.PickupWarehouseId,
                RequiredCertifications: Array.Empty<CertificationType>()),
            cancellationToken);

        if (!selection.IsAssigned)
        {
            _logger.LogWarning(
                "[ManualDispatch] Order {OrderId} group {G} — no operator: {Reason}",
                request.DeliveryOrderId, request.GroupIndex, selection.RejectionReason);
            return Result<DispatchGroupOutcome>.Failure(
                $"Manual dispatch failed — {selection.RejectionReason}");
        }
        var op = selection.Operator!;

        // ── 2. Persist Trip ──────────────────────────────────────────
        // VendorOrderKey is null for Manual — no external system mints
        // a key. The Trip aggregate's CreateForEnvelope skips the
        // AmrTripExtension when vendorOrderKey is null (Phase 3b
        // behaviour we explicitly want here).
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
            pickupWarehouseId: request.PickupWarehouseId,
            dropWarehouseId: request.DropWarehouseId);
        await _trips.AddAsync(trip, cancellationToken);

        // ── 3. ManualTripExtension + SLA stamps ──────────────────────
        var now = DateTime.UtcNow;
        var ackDeadline = now.AddMinutes(_options.AckSlaMinutes);
        // Pickup deadline measured from now (operator hasn't ack'd yet) —
        // gives them a single end-to-end window the watchdog can flag if
        // they ack but never move.
        var pickupDeadline = now.AddMinutes(_options.AckSlaMinutes + _options.PickupSlaMinutes);
        var dropDeadline = now.AddMinutes(
            _options.AckSlaMinutes + _options.PickupSlaMinutes + _options.DropSlaMinutes);
        // If the upstream order has its own LatestUtc, prefer that as the
        // drop deadline — customer SLA wins over our default window.
        if (request.SlaDeadline.HasValue && request.SlaDeadline.Value < dropDeadline)
            dropDeadline = request.SlaDeadline.Value;

        var extension = ManualTripExtension.AssignToOperator(
            tripId: trip.Id,
            operatorId: op.Id,
            ackDeadline: ackDeadline,
            pickupDeadline: pickupDeadline,
            dropDeadline: dropDeadline);
        await _extensions.AddAsync(extension, cancellationToken);

        // ── 4. Bind operator → trip ──────────────────────────────────
        // GetEligibleForAssignmentAsync returned a tracking-attached
        // entity; AssignToTrip mutates + fires the domain event.
        // Reload via Id so we get a tracked instance even if the
        // policy returned a detached one (cert filter path loads via
        // GetByIdWithDetailsAsync which DOES track).
        var trackedOp = await _operators.GetByIdAsync(op.Id, cancellationToken);
        if (trackedOp is null)
        {
            // Defensive: someone deleted the operator between selection
            // and assignment. Fail the dispatch — the consumer's failure
            // path will roll back the trip + extension.
            return Result<DispatchGroupOutcome>.Failure(
                $"Operator {op.Id} disappeared between selection and assignment.");
        }
        trackedOp.AssignToTrip(trip.Id);

        // ── 5. Persist all three aggregates in one transaction ───────
        // SaveChanges on any of the three repos flushes the shared
        // EF change tracker for that DbContext; the Operator save also
        // commits the Trip + Extension changes since they share the
        // module's outbox interceptor. Save via the operator repo so
        // its tracked instance is the one that drives the SaveChanges.
        await _trips.UpdateAsync(trip, cancellationToken);
        await _operators.SaveChangesAsync(cancellationToken);
        await _extensions.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[ManualDispatch] ✓ Order {OrderId} group {G} → trip {TripId}, operator {EmployeeCode} ({OperatorId}), ack-by {AckBy:O}",
            request.DeliveryOrderId, request.GroupIndex, trip.Id, op.EmployeeCode, op.Id, ackDeadline);

        // ── 6. Bind items to trip ───────────────────────────────────
        // Without this the Trip's items list stays empty AND the
        // TripCompletedIntegrationEvent → ItemStatus=Delivered projection
        // matches zero rows, leaving items stuck at PENDING after the
        // operator completes the trip. AMR does the same via
        // DispatchOrderTemplateService — Manual mirrors that contract.
        //
        // Best-effort — a binding failure logs a warning but doesn't
        // roll back the dispatch. The Trip is already real on the
        // vendor (operator) side; ops can manually re-bind via SQL if
        // the matching path silently failed.
        try
        {
            var bindResult = await _sender.Send(new AssignItemsToTripCommand(
                OrderId: request.DeliveryOrderId,
                TripId: trip.Id,
                AttemptNumber: request.AttemptNumber,
                PickupStationId: request.PickupStationId,
                DropStationId: request.DropStationId,
                PickupWarehouseId: request.PickupWarehouseId,
                DropWarehouseId: request.DropWarehouseId), cancellationToken);
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
                "[ManualDispatch] Item binding threw for trip {TripId} — items will stay unbound until manual recovery.",
                trip.Id);
        }

        // ── 7. Push notification (best-effort) ───────────────────────
        try
        {
            var shortOrderId = request.DeliveryOrderId.ToString()[..8];
            var payload = new PushNotificationPayload(
                Title: string.Format(_options.PushTitleTemplate, shortOrderId),
                Body: _options.PushBodyTemplate,
                Url: _options.PushTargetUrl,
                Tag: $"trip-{trip.Id}");
            var fanout = await _push.SendToOperatorAsync(op.Id, payload, cancellationToken);
            _logger.LogInformation(
                "[ManualDispatch] Push fanout for operator {OperatorId}: {Sent} sent, {Failed} failed",
                op.Id, fanout.Sent, fanout.Failed);
        }
        catch (Exception ex)
        {
            // Don't fail the dispatch — the operator's PWA polls
            // /trips/assigned so they'll see it on next refresh. Push
            // is a latency optimization, not a delivery guarantee.
            _logger.LogWarning(ex,
                "[ManualDispatch] Push notification failed for operator {OperatorId} — trip {TripId} dispatched OK regardless.",
                op.Id, trip.Id);
        }

        return Result<DispatchGroupOutcome>.Success(new DispatchGroupOutcome(
            TripId: trip.Id,
            VendorOrderKey: null,         // Manual has no external vendor key
            TemplateName: "manual"));      // Stable label for dashboards
    }
}
