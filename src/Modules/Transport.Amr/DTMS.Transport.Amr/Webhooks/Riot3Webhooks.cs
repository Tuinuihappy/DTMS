using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Domain.Services;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Fleet.IntegrationEvents;
using DTMS.SharedKernel;
using DTMS.Transport.Abstractions.Services;
using DTMS.Transport.Amr.Models;
using DTMS.Transport.Amr.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DTMS.Transport.Amr.Webhooks;

public static class Riot3Webhooks
{
    public static void MapRiot3Webhooks(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/webhooks/riot3").WithTags("Webhooks");

        // RIOT3.0 v4 /api/v4/notify callback — task / subTask / vehicle events.
        //
        // Auth: RIOT3 has no built-in webhook signature/header support, so
        // the auth filter layers an IP allowlist + URL-path secret. The
        // optional {secret} segment lets ops configure RIOT3 with the
        // notification URL "/api/webhooks/riot3/notify/{secret}" without
        // touching DTMS — see Riot3WebhookAuthFilter for the gates.
        group.MapPost("/notify/{secret?}", async (
            Riot3NotifyPayload payload,
            IVendorAdapterOutbox outbox,
            IVehicleIdentityResolver vehicleIdentityResolver,
            ITripRepository tripRepository,
            ITripMissionEventRepository missionEventRepository,
            ITripItemSnapshotProvider tripItemSnapshotProvider,
            DTMS.Facility.Application.Services.IFacilityReadService facilityReadService,
            DTMS.Dispatch.Application.Services.IDeliveryOrderStatusReader orderReader,
            ITripRealtimePublisher realtimePublisher,
            DTMS.SharedKernel.Diagnostics.WorkflowMetrics metrics,
            ILogger<Riot3NotifyPayload> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogDebug("RIOT3 notify: type={Type} taskEvent={TaskEvent} vehicleEvent={VehicleEvent}",
                payload.Type, payload.TaskEventType, payload.VehicleEventType);

            // Channel-liveness pulse — count EVERY frame before any routing or
            // correlation (incl. frames for RIOT3's own charge/park orders).
            // The webhook-silence alert fires when this stops moving while
            // trips are in flight; see ops/prometheus/rules/webhook-silence.yml.
            var notifyType = NormalizeNotifyType(payload.Type);
            metrics.RecordNotifyFrame(string.IsNullOrEmpty(notifyType) ? "other" : notifyType);

            switch (notifyType)
            {
                case "task":
                    await HandleTaskEvent(payload, outbox, tripRepository, tripItemSnapshotProvider, logger, cancellationToken);
                    break;

                case "subtask":
                    await HandleSubTaskEvent(payload, tripRepository, missionEventRepository, facilityReadService, orderReader, realtimePublisher, metrics, logger, cancellationToken);
                    break;

                case "vehicle":
                    await HandleVehicleEvent(payload, outbox, vehicleIdentityResolver, logger, cancellationToken);
                    break;

                default:
                    logger.LogWarning("Unknown RIOT3 notify type: {Type}", payload.Type);
                    break;
            }

            await outbox.SaveChangesAsync(cancellationToken);
            return Results.Ok();
        }).AddEndpointFilter<Riot3WebhookAuthFilter>();
    }

    // ── task event handlers ──────────────────────────────────────────────────

    private static string NormalizeNotifyType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "task" or "tasknotify" => "task",
        "subtask" or "subtasknotify" => "subtask",
        "vehicle" or "vehiclenotify" => "vehicle",
        var value => value ?? string.Empty
    };

    private static async Task HandleTaskEvent(
        Riot3NotifyPayload payload,
        IVendorAdapterOutbox outbox,
        ITripRepository tripRepository,
        ITripItemSnapshotProvider tripItemSnapshotProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var upperKey = payload.Task?.UpperKey;
        var orderKey = payload.Task?.Key ?? string.Empty;

        // Envelope-dispatched orders use a composite upperKey
        // ("{deliveryOrderId:N}-G{groupIndex}"). All RIOT3 trips are
        // envelope-dispatched now (legacy per-task path removed in Phase b7).
        if (!EnvelopeUpperKey.TryParse(upperKey, out _, out _))
        {
            logger.LogWarning("RIOT3 task event has unrecognized upperKey format: {UpperKey} — ignored.", upperKey);
            return;
        }

        await HandleEnvelopeTaskEvent(payload, upperKey!, orderKey, tripRepository, tripItemSnapshotProvider, logger, cancellationToken);
    }

    // ── envelope-dispatched task events ──────────────────────────────────────
    // For envelope-dispatched trips, upperKey is the composite DTMS key
    // ("{orderId:N}-G{groupIndex}"). We look up the Trip we persisted at
    // dispatch time and update its status directly via the vendor lifecycle
    // methods. No integration event propagation yet — Phase (b6) wires that
    // into DeliveryOrder.MarkAsCompleted.
    private static async Task HandleEnvelopeTaskEvent(
        Riot3NotifyPayload payload,
        string upperKey,
        string orderKey,
        ITripRepository tripRepository,
        ITripItemSnapshotProvider tripItemSnapshotProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var trip = await tripRepository.GetByUpperKeyAsync(upperKey, cancellationToken);
        if (trip is null)
        {
            logger.LogWarning(
                "[EnvelopeWebhook] No Trip found for upperKey {UpperKey} (vendor orderKey {OrderKey}, event {Event}) — webhook ignored.",
                upperKey, orderKey, payload.TaskEventType);
            return;
        }

        var eventType = payload.TaskEventType?.ToUpperInvariant();
        if (!await TryApplyTaskEventAsync(trip, payload, eventType, upperKey, tripItemSnapshotProvider, logger, cancellationToken))
            return;

        try
        {
            await tripRepository.UpdateAsync(trip, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another writer (an operator command mid-save, the reconciler, or
            // a sibling frame) committed between our load and save. Purge the
            // tracker (a failed save leaves drained OutboxMessage rows queued
            // as Added — saving them would resurrect the duplicate event the
            // token just blocked), reload fresh and re-apply: the domain
            // guards / IOE catch turn an already-applied transition into a
            // no-op. A second consecutive conflict propagates (webhook 500;
            // frames are fire-and-forget and the reconciler heals in ≤60s).
            tripRepository.ResetTracking();
            trip = await tripRepository.GetByUpperKeyAsync(upperKey, cancellationToken);
            if (trip is null)
            {
                logger.LogWarning(
                    "[EnvelopeWebhook] Trip for upperKey {UpperKey} disappeared during conflict retry — webhook ignored.",
                    upperKey);
                return;
            }

            if (!await TryApplyTaskEventAsync(trip, payload, eventType, upperKey, tripItemSnapshotProvider, logger, cancellationToken))
                return;

            await tripRepository.UpdateAsync(trip, cancellationToken);
            logger.LogInformation(
                "[EnvelopeWebhook] Trip {TripId} event {Event} applied after conflict retry (upperKey {UpperKey})",
                trip.Id, eventType, upperKey);
        }
    }

    // Applies one RIOT3 task event to the aggregate. Returns true when the
    // trip was mutated and needs saving; false when the event carries no
    // state change or the transition was rejected (already applied /
    // superseded — logged and swallowed). internal for unit tests and reuse
    // by the conflict-retry path above.
    internal static async Task<bool> TryApplyTaskEventAsync(
        Trip trip,
        Riot3NotifyPayload payload,
        string? eventType,
        string upperKey,
        ITripItemSnapshotProvider tripItemSnapshotProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (eventType)
            {
                case "TASK_PROCESSING":
                    // RIOT3's processingVehicle.key is the vendor deviceKey
                    // (a string like "Delta6FAN1" / "SEER-001"), not a Guid.
                    // Store it verbatim on Trip.VendorVehicleKey so operator
                    // dashboards can see who picked up the trip. Trip.VehicleId
                    // (DTMS Guid) intentionally stays null in this flow — a
                    // Fleet lookup is left for a future iteration.
                    var vehKey = payload.Task?.ProcessingVehicle?.Key;
                    var vehName = payload.Task?.ProcessingVehicle?.Name;
                    // Phase P5.3 — snapshot items bound to this trip so
                    // TripItemsProjector can materialize dispatch.TripItems
                    // for the operator drawer.
                    var itemSnapshots = await tripItemSnapshotProvider.GetForTripAsync(trip.Id, cancellationToken);
                    trip.MarkVendorStarted(vehicleId: null, vendorVehicleKey: vehKey, vendorVehicleName: vehName, items: itemSnapshots);
                    logger.LogInformation("[EnvelopeWebhook] Trip {TripId} started (upperKey {UpperKey}, vendor vehicle '{VehKey}' / '{VehName}', items={ItemCount})",
                        trip.Id, upperKey, vehKey ?? "(none)", vehName ?? "(none)", itemSnapshots.Count);
                    break;

                case "TASK_FINISHED":
                    trip.MarkVendorCompleted();
                    logger.LogInformation("[EnvelopeWebhook] ✓ Trip {TripId} completed (upperKey {UpperKey})",
                        trip.Id, upperKey);
                    break;

                case "TASK_FAILED":
                    var failReason = payload.Task?.FailReason?.ErrorDescription
                                     ?? payload.Task?.FailReason?.ErrorCode
                                     ?? "vendor reported failure";
                    trip.MarkVendorFailed(failReason);
                    logger.LogWarning("[EnvelopeWebhook] Trip {TripId} failed (upperKey {UpperKey}): {Reason}",
                        trip.Id, upperKey, failReason);
                    break;

                case "TASK_CANCELED":
                    // Vendor cancel is treated the same as operator cancel —
                    // Trip moves to Cancelled and the DeliveryOrder is left
                    // untouched so it remains eligible for re-dispatch.
                    // Distinct from TASK_FAILED which propagates to mark the
                    // DeliveryOrder as Failed via TripFailedConsumer.
                    var cancelReason = payload.Task?.CancelReason ?? "vendor cancelled";
                    trip.Cancel(cancelReason);
                    logger.LogInformation("[EnvelopeWebhook] Trip {TripId} cancelled by vendor (upperKey {UpperKey}): {Reason}",
                        trip.Id, upperKey, cancelReason);
                    break;

                case "TASK_HANG":
                case "TASK_HELD":
                    // Vendor paused the order — the flavour drives the status:
                    // TASK_HELD = operator pause → Trip.Held (resume sends
                    // CONTINUE_FROM_HELD); TASK_HANG = system pause (e.g.
                    // E230025 mode change) → Trip.Hang (CONTINUE_FROM_HANG).
                    // Crossing them returns E639999. Trip.Pause also handles
                    // mid-pause re-flavouring (vendor drift Hang↔Held).
                    var hangReason = payload.Task?.HangReason;
                    var pauseSource = eventType == "TASK_HANG"
                        ? DTMS.Dispatch.Domain.Enums.VendorPauseSource.Hang
                        : DTMS.Dispatch.Domain.Enums.VendorPauseSource.Held;
                    trip.Pause(pauseSource);
                    logger.LogInformation("[EnvelopeWebhook] Trip {TripId} paused by vendor (upperKey {UpperKey}) event={Event} source={Source} reason={Reason}",
                        trip.Id, upperKey, eventType, pauseSource, hangReason ?? "(none)");
                    break;

                case "TASK_HANG_TO_CONTINUE":
                case "TASK_HELD_TO_CONTINUE":
                    // Vendor resumed from hang/held — pair with the
                    // HANG/HELD events above. Idempotent: if Trip was never
                    // paused (vendor recovered before we received HANG)
                    // Trip.Resume throws and we just log + ignore.
                    trip.Resume();
                    logger.LogInformation("[EnvelopeWebhook] Trip {TripId} resumed by vendor (upperKey {UpperKey}) event={Event}",
                        trip.Id, upperKey, eventType);
                    break;

                case "TASK_REJECTED":
                    // Vendor refused the task post-dispatch (rare — usually
                    // POST /orders catches bad payloads; REJECTED would be
                    // a late vendor-side issue). Distinct Rejected status;
                    // the DeliveryOrder still propagates to Failed via
                    // TripRejectedIntegrationEventV1.
                    var rejectReason = payload.Task?.FailReason?.ErrorDescription
                                       ?? payload.Task?.FailReason?.ErrorCode
                                       ?? "vendor rejected task";
                    trip.MarkVendorRejected(rejectReason);
                    logger.LogWarning("[EnvelopeWebhook] Trip {TripId} rejected by vendor (upperKey {UpperKey}): {Reason}",
                        trip.Id, upperKey, rejectReason);
                    break;

                default:
                    // TASK_CREATE / TASK_QUEUEING / SUB_TASK_* land here —
                    // no state change applied (Trip already exists in DTMS
                    // before dispatch; queueing is intermediate).
                    logger.LogDebug("[EnvelopeWebhook] Trip {TripId} event {Event} — no state change applied.",
                        trip.Id, eventType);
                    return false;
            }
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("[EnvelopeWebhook] Trip {TripId} state transition rejected for event {Event}: {Error}",
                trip.Id, eventType, ex.Message);
            return false;
        }

        return true;
    }

    // Persist per-mission lifecycle events for the trip detail UI.
    // Idempotent at the repository (UNIQUE (TripId, MissionKey, State));
    // the reconciler does the same upsert so dropped webhooks recover.
    //
    // Mapping RIOT3 sub-task event → DTMS TripMissionEvent.State:
    //   SUB_TASK_PROCESSING → "PROCESSING"
    //   SUB_TASK_FINISHED   → "FINISHED"
    //   SUB_TASK_FAILED     → "FAILED"
    //   SUB_TASK_CANCELED   → "CANCELED"
    private static async Task HandleSubTaskEvent(
        Riot3NotifyPayload payload,
        ITripRepository tripRepository,
        ITripMissionEventRepository missionEventRepository,
        DTMS.Facility.Application.Services.IFacilityReadService facilityReadService,
        DTMS.Dispatch.Application.Services.IDeliveryOrderStatusReader orderReader,
        ITripRealtimePublisher realtimePublisher,
        DTMS.SharedKernel.Diagnostics.WorkflowMetrics metrics,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var subTask = payload.SubTask;
        var subTaskKey = subTask?.Key;
        var eventType = payload.TaskEventType?.ToUpperInvariant();

        if (subTask is null || string.IsNullOrWhiteSpace(subTaskKey))
        {
            logger.LogDebug("RIOT3 sub-task event {Event} has no SubTask payload — ignored", eventType);
            return;
        }

        // RIOT3 omits the parent `task` object from sub-task event frames,
        // so payload.Task?.UpperKey is null in practice. Fall back to the
        // vendor-side order key carried on subTask.taskKey (echoes
        // Trip.VendorOrderKey) when the DTMS upperKey is absent.
        //
        // Hot-path projection: this handler fires ~22 times per trip but
        // only the few MOVE-FINISHED frames that can still flip pickup/drop
        // need the tracked Trip aggregate (heavy Includes). Resolve a cheap
        // no-tracking lookup here; the full load happens behind the
        // detector gate at the bottom.
        var upperKey = payload.Task?.UpperKey;
        var vendorOrderKey = subTask.TaskKey;

        var lookup = await tripRepository.GetSubTaskLookupAsync(upperKey, vendorOrderKey, cancellationToken);
        if (lookup is null)
        {
            logger.LogWarning("[SubTaskWebhook] No Trip found for subTask {SubTaskKey} (upperKey {UpperKey}, vendorOrderKey {VendorOrderKey}, event {Event}) — ignored.",
                subTaskKey, upperKey ?? "(none)", vendorOrderKey ?? "(none)", eventType);
            return;
        }
        var tripId = lookup.Value.Id;

        var state = eventType switch
        {
            "SUB_TASK_PROCESSING" => "PROCESSING",
            "SUB_TASK_FINISHED"   => "FINISHED",
            "SUB_TASK_FAILED"     => "FAILED",
            "SUB_TASK_CANCELED"   => "CANCELED",
            _                     => null
        };
        if (state is null)
        {
            logger.LogDebug("RIOT3 sub-task event {Event} not mapped to a mission state — ignored", eventType);
            return;
        }

        var failResult = subTask.FailResult;
        var actResult  = subTask.ActResult;
        var stationName = subTask.Station?.Station?.Name;
        var stationId   = subTask.Station?.Station?.Id;

        // MissionIndex isn't on the sub-task payload — fall back to 0 so
        // the row is still stored. The detail endpoint orders by
        // ChangeStateTime when index ties.
        //
        // Field semantics (station-by-type, time-by-state) live in the
        // shared factory so this path can never drift from the reconciler.
        var missionEvent = Riot3MissionEventFactory.Create(
            tripId: tripId,
            missionIndex: 0,
            missionKey: subTaskKey,
            missionType: subTask.SubTaskType,
            state: state,
            startedTime: subTask.StartedTime,
            finishedTime: subTask.FinishedTime,
            stationName: stationName,
            actionName: subTask.ActionName,
            actionType: subTask.ActionType,
            resultCode: failResult?.ErrorCode ?? actResult?.Code,
            errorMessage: failResult?.ErrorDescription,
            logger: logger);

        var inserted = await missionEventRepository.AddIfNotExistsAsync(missionEvent, cancellationToken);
        if (inserted)
        {
            logger.LogInformation("[SubTaskWebhook] Trip {TripId} mission {MissionKey} → {State}",
                tripId, subTaskKey, state);

            // Push to operator drawer so the Mission Timeline + failure
            // banner update without a manual refresh. Fire-and-forget by
            // design — publisher swallows transport errors and the UI
            // catches up on next REST refetch.
            await realtimePublisher.PublishMissionUpdatedAsync(
                tripId,
                new TripMissionEventDto(
                    MissionIndex: missionEvent.MissionIndex,
                    MissionKey: missionEvent.MissionKey,
                    MissionType: missionEvent.MissionType,
                    State: missionEvent.State,
                    StationName: missionEvent.StationName,
                    ActionName: missionEvent.ActionName,
                    ActionType: missionEvent.ActionType,
                    ResultCode: missionEvent.ResultCode,
                    ErrorMessage: missionEvent.ErrorMessage,
                    ChangeStateTime: missionEvent.ChangeStateTime,
                    ReceivedAt: missionEvent.ReceivedAt),
                cancellationToken);
        }
        else
        {
            logger.LogDebug("[SubTaskWebhook] Trip {TripId} mission {MissionKey} {State} — duplicate, skipped",
                tripId, subTaskKey, state);
        }

        // ── Item-Picked / DroppedOff detection ─────────────────────────
        // Once a pickup/drop sub-mission finishes at the trip's pickup OR
        // drop station, fire the matching domain event so the DeliveryOrder
        // side flips item status. Only MOVE qualifies:
        //   • MOVE FINISHED → robot arrived at the station. Treating arrival
        //     as pickup/drop is a small semantic stretch (robot is at the
        //     dock, operator may still be loading), but it's the only signal
        //     RIOT3 emits before TASK_FINISHED gaps every item straight to
        //     Delivered — and the only one with a trustworthy station.
        //   • ACT FINISHED is deliberately NOT a signal: the station object
        //     on ACT frames is a stale last-registered dock that lags the
        //     robot by one leg (see TripStationTransitionDetector for the
        //     trip 5018 evidence) — it could fire pickup/drop on the wrong
        //     visit.
        // Ignored when:
        //   • state != FINISHED         (only completion counts)
        //   • mission type not MOVE     (ACT stations untrustworthy, others carry none)
        //   • duplicate webhook         (already-stored row, no event)
        //   • trip has no pickup/drop   (pre-Gap-3 trip — degrade silently)
        //   • station id missing
        //   • station resolves to neither pickup nor drop
        //
        // Resolves via the vendor-side station id (VendorRef) rather than
        // station name: RIOT3 emits the name in its own casing ("Station165")
        // which won't match the upper-cased Code DTMS stores ("STATION165"),
        // and IDs are stable across vendor renames.
        // Fire-once pickup/drop detection, shared with the reconciler safety net
        // (Riot3ReconciliationService) via TripStationTransitionDetector so a
        // dropped sub-task webhook doesn't lose the pickup/drop signal. Gated on
        // `inserted` so a duplicate webhook (already-stored row) doesn't re-run
        // detection; the fire-once guard on the Trip covers the rest.
        //
        // Projection gate: the tracked Trip aggregate (heavy Includes) is
        // loaded ONLY when this frame could actually flip a signal —
        // MOVE FINISHED while pickup or drop is still pending per the cheap
        // lookup. ACT/PROCESSING frames and post-completion MOVEs (the vast
        // majority) never touch the aggregate. The lookup snapshot may be
        // stale under concurrency, but the detector re-checks fire-once on
        // the freshly loaded Trip, so a stale "still pending" only costs one
        // extra load — never a double fire.
        var fullTripLoad = state == "FINISHED"
            && inserted
            && string.Equals(subTask.SubTaskType, "MOVE", StringComparison.OrdinalIgnoreCase)
            && (lookup.Value.VendorPickedUpAt is null || lookup.Value.VendorDroppedAt is null);
        // full-load ratio observability (dtms.workflow.webhook_*): should sit
        // around 0.1-0.2 — a sustained climb means the gate stopped
        // short-circuiting for some template shape.
        metrics.RecordWebhookSubTaskFrame(fullTripLoad);

        if (fullTripLoad)
        {
            Trip? trip = null;
            if (!string.IsNullOrWhiteSpace(upperKey))
                trip = await tripRepository.GetByUpperKeyAsync(upperKey, cancellationToken);
            if (trip is null && !string.IsNullOrWhiteSpace(vendorOrderKey))
                trip = await tripRepository.GetByVendorOrderKeyAsync(vendorOrderKey, cancellationToken);
            if (trip is null)
            {
                // Trip vanished between lookup and load (deleted mid-flight)
                // — the mission row is already stored; nothing to mutate.
                logger.LogWarning("[SubTaskWebhook] Trip {TripId} disappeared before pickup/drop detection — skipped", tripId);
                return;
            }

            if (await TripStationTransitionDetector.TryApplyAsync(
                    trip, subTask.SubTaskType, state, stationId,
                    facilityReadService, orderReader, missionEvent.ChangeStateTime, logger, cancellationToken))
            {
                try
                {
                    await tripRepository.UpdateAsync(trip, cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // A task frame / command / reconciler write landed between
                    // our load and save. Purge the tracker (drops the drained
                    // outbox rows a failed save leaves behind), reload fresh
                    // and re-run the detector — its fire-once re-check on the
                    // fresh trip no-ops if the other writer already recorded
                    // the pickup/drop signal. Second conflict propagates
                    // (webhook 500; reconciler heals in ≤60s).
                    tripRepository.ResetTracking();
                    Trip? freshTrip = null;
                    if (!string.IsNullOrWhiteSpace(upperKey))
                        freshTrip = await tripRepository.GetByUpperKeyAsync(upperKey, cancellationToken);
                    if (freshTrip is null && !string.IsNullOrWhiteSpace(vendorOrderKey))
                        freshTrip = await tripRepository.GetByVendorOrderKeyAsync(vendorOrderKey, cancellationToken);
                    if (freshTrip is null)
                    {
                        logger.LogWarning("[SubTaskWebhook] Trip {TripId} disappeared during conflict retry — skipped", tripId);
                        return;
                    }

                    if (await TripStationTransitionDetector.TryApplyAsync(
                            freshTrip, subTask.SubTaskType, state, stationId,
                            facilityReadService, orderReader, missionEvent.ChangeStateTime, logger, cancellationToken))
                    {
                        await tripRepository.UpdateAsync(freshTrip, cancellationToken);
                        logger.LogInformation(
                            "[SubTaskWebhook] Trip {TripId} pickup/drop applied after conflict retry", freshTrip.Id);
                    }
                }
            }
        }
    }

    private static async Task HandleVehicleEvent(
        Riot3NotifyPayload payload,
        IVendorAdapterOutbox outbox,
        IVehicleIdentityResolver vehicleIdentityResolver,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var vehicle = payload.VehicleInfo;
        if (vehicle == null || string.IsNullOrWhiteSpace(vehicle.Key))
        {
            logger.LogDebug("RIOT3 vehicle event ignored — missing vehicleInfo.key");
            return;
        }

        var vehicleId = await vehicleIdentityResolver.ResolveVehicleIdAsync("riot3", vehicle.Key, cancellationToken);
        if (!vehicleId.HasValue)
        {
            logger.LogWarning("RIOT3 vehicle event ignored because deviceKey {DeviceKey} is not mapped", vehicle.Key);
            return;
        }

        var canonicalState = MapRiotSystemState(vehicle.SystemState);
        var batteryPct = (vehicle.BatteryState?.BatteryCharge ?? 0) / 100.0;

        await outbox.AddAsync(new VehicleStateChangedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, vehicleId.Value, canonicalState, batteryPct, null), cancellationToken);

        // Emergency = eStop is anything other than NONE (AUTOACK/MANUAL/REMOTE)
        var eStop = vehicle.SafetyState?.EStop;
        if (!string.IsNullOrEmpty(eStop) && !eStop.Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("RIOT3 emergency triggered for vehicle {VehicleId}: eStop={EStop} event={Event}",
                vehicleId.Value, eStop, payload.VehicleEventType);
        }

        if (batteryPct < 0.20)
        {
            await outbox.AddAsync(new VehicleBatteryLowIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, vehicleId.Value, Guid.Empty, batteryPct), cancellationToken);
        }
    }

    private static string MapRiotSystemState(string? systemState) => systemState?.ToUpper() switch
    {
        "IDLE" => "Idle",
        "BUSY" or "RUNNING" or "EXECUTING" => "Moving",
        "ERROR" => "Error",
        "CHARGING" => "Charging",
        _ => "Offline"
    };
}
