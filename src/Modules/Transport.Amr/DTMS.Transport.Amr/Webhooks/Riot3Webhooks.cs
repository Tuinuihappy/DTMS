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
            ILogger<Riot3NotifyPayload> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogDebug("RIOT3 notify: type={Type} taskEvent={TaskEvent} vehicleEvent={VehicleEvent}",
                payload.Type, payload.TaskEventType, payload.VehicleEventType);

            switch (NormalizeNotifyType(payload.Type))
            {
                case "task":
                    await HandleTaskEvent(payload, outbox, tripRepository, tripItemSnapshotProvider, logger, cancellationToken);
                    break;

                case "subtask":
                    await HandleSubTaskEvent(payload, tripRepository, missionEventRepository, facilityReadService, orderReader, realtimePublisher, logger, cancellationToken);
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
                    // Vendor paused the order — mirror to Trip.Paused, but
                    // capture WHICH flavour so the resume handler picks the
                    // matching RIOT3 command. TASK_HELD = operator pause →
                    // CONTINUE_FROM_HELD; TASK_HANG = system pause (e.g.
                    // E230025 mode change) → CONTINUE_FROM_HANG. Crossing
                    // them returns E639999 "multi-level template fill error".
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
                    // a late vendor-side issue). Treat as failure so the
                    // DeliveryOrder reflects the operational outcome.
                    var rejectReason = payload.Task?.FailReason?.ErrorDescription
                                       ?? payload.Task?.FailReason?.ErrorCode
                                       ?? "vendor rejected task";
                    trip.MarkVendorFailed(rejectReason);
                    logger.LogWarning("[EnvelopeWebhook] Trip {TripId} rejected by vendor (upperKey {UpperKey}): {Reason}",
                        trip.Id, upperKey, rejectReason);
                    break;

                default:
                    // TASK_CREATE / TASK_QUEUEING / SUB_TASK_* land here —
                    // no state change applied (Trip already exists in DTMS
                    // before dispatch; queueing is intermediate).
                    logger.LogDebug("[EnvelopeWebhook] Trip {TripId} event {Event} — no state change applied.",
                        trip.Id, eventType);
                    return;
            }
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("[EnvelopeWebhook] Trip {TripId} state transition rejected for event {Event}: {Error}",
                trip.Id, eventType, ex.Message);
            return;
        }

        await tripRepository.UpdateAsync(trip, cancellationToken);
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
        var upperKey = payload.Task?.UpperKey;
        var vendorOrderKey = subTask.TaskKey;

        Trip? trip = null;
        if (!string.IsNullOrWhiteSpace(upperKey))
            trip = await tripRepository.GetByUpperKeyAsync(upperKey, cancellationToken);
        if (trip is null && !string.IsNullOrWhiteSpace(vendorOrderKey))
            trip = await tripRepository.GetByVendorOrderKeyAsync(vendorOrderKey, cancellationToken);

        if (trip is null)
        {
            logger.LogWarning("[SubTaskWebhook] No Trip found for subTask {SubTaskKey} (upperKey {UpperKey}, vendorOrderKey {VendorOrderKey}, event {Event}) — ignored.",
                subTaskKey, upperKey ?? "(none)", vendorOrderKey ?? "(none)", eventType);
            return;
        }

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

        // RIOT3 timestamps come as strings; tolerate missing or malformed.
        var changeTime = ParseRiot3Time(subTask.FinishedTime)
                         ?? ParseRiot3Time(subTask.StartedTime)
                         ?? DateTime.UtcNow;

        // MissionIndex isn't on the sub-task payload — fall back to 0 so
        // the row is still stored. The detail endpoint orders by
        // ChangeStateTime when index ties.
        var missionEvent = TripMissionEvent.Record(
            tripId: trip.Id,
            missionIndex: 0,
            missionKey: subTaskKey,
            missionType: string.IsNullOrWhiteSpace(subTask.SubTaskType) ? "UNKNOWN" : subTask.SubTaskType,
            state: state,
            changeStateTime: changeTime,
            stationName: stationName,
            actionName: subTask.ActionName,
            actionType: subTask.ActionType,
            resultCode: failResult?.ErrorCode ?? actResult?.Code,
            errorMessage: failResult?.ErrorDescription);

        var inserted = await missionEventRepository.AddIfNotExistsAsync(missionEvent, cancellationToken);
        if (inserted)
        {
            logger.LogInformation("[SubTaskWebhook] Trip {TripId} mission {MissionKey} → {State}",
                trip.Id, subTaskKey, state);

            // Push to operator drawer so the Mission Timeline + failure
            // banner update without a manual refresh. Fire-and-forget by
            // design — publisher swallows transport errors and the UI
            // catches up on next REST refetch.
            await realtimePublisher.PublishMissionUpdatedAsync(
                trip.Id,
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
                trip.Id, subTaskKey, state);
        }

        // ── Item-Picked / DroppedOff detection ─────────────────────────
        // Once a pickup/drop sub-mission finishes at the trip's pickup OR
        // drop station, fire the matching domain event so the DeliveryOrder
        // side flips item status. Both ACT and MOVE qualify:
        //   • ACT FINISHED  → vendor ran a pickup/drop action (e.g. lift,
        //     load, dispense) AT the station — items physically loaded/dropped.
        //   • MOVE FINISHED → robot arrived at the station. For
        //     operator-confirm templates (e.g. Confirm-X-to-Y, which use
        //     WaitingConfirm — an ACT with no stationId), the MOVE
        //     completion is the closest available "reached pickup/drop"
        //     signal: WaitingConfirm itself fires no station-tagged event.
        //     Treating MOVE arrival as pickup/drop is a small semantic
        //     stretch (robot is at the dock, operator may still be loading),
        //     but it's the only signal RIOT3 emits before TASK_FINISHED
        //     gaps every item straight to Delivered.
        // Ignored when:
        //   • state != FINISHED         (only completion counts)
        //   • mission type not ACT/MOVE (other types carry no station)
        //   • duplicate webhook         (already-stored row, no event)
        //   • trip has no pickup/drop   (pre-Gap-3 trip — degrade silently)
        //   • station id missing        (ACT WaitingConfirm-style → no station bound)
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
        if (state == "FINISHED" && inserted)
        {
            if (await TripStationTransitionDetector.TryApplyAsync(
                    trip, subTask.SubTaskType, state, stationId,
                    facilityReadService, orderReader, changeTime, logger, cancellationToken))
            {
                await tripRepository.UpdateAsync(trip, cancellationToken);
            }
        }
    }

    private static DateTime? ParseRiot3Time(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var dt)
            ? dt
            : null;
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
