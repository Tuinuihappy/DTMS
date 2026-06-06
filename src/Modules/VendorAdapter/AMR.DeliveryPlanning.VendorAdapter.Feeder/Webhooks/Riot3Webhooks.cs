using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.Fleet.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Feeder.Webhooks;

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
            AMR.DeliveryPlanning.Facility.Application.Services.IFacilityReadService facilityReadService,
            ILogger<Riot3NotifyPayload> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogDebug("RIOT3 notify: type={Type} taskEvent={TaskEvent} vehicleEvent={VehicleEvent}",
                payload.Type, payload.TaskEventType, payload.VehicleEventType);

            switch (NormalizeNotifyType(payload.Type))
            {
                case "task":
                    await HandleTaskEvent(payload, outbox, tripRepository, logger, cancellationToken);
                    break;

                case "subtask":
                    await HandleSubTaskEvent(payload, tripRepository, missionEventRepository, facilityReadService, logger, cancellationToken);
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

        // Legacy simple status endpoint (kept for backward compatibility)
        group.MapPost("/status", async (
            RiotStatusPayload payload,
            IVendorAdapterOutbox outbox,
            ILogger<RiotStatusPayload> logger,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(payload.RobotId, out var vehicleId))
                return Results.BadRequest("Invalid RobotId format.");

            await outbox.AddAsync(new VehicleStateChangedIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, vehicleId,
                MapRiotState(payload.State), payload.Battery,
                payload.CurrentNode != null && Guid.TryParse(payload.CurrentNode, out var nodeId) ? nodeId : null),
                cancellationToken);
            await outbox.SaveChangesAsync(cancellationToken);

            return Results.Ok();
        });

        // RIOT3.0 action callback mid-task
        group.MapPost("/action-callback", (Riot3ActionCallbackPayload payload, ILogger<Riot3ActionCallbackPayload> logger) =>
        {
            logger.LogInformation("RIOT3 action callback: taskId={TaskId} result={Result}", payload.TaskId, payload.Result);
            return Results.Ok();
        });
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

        await HandleEnvelopeTaskEvent(payload, upperKey!, orderKey, tripRepository, logger, cancellationToken);
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
                    trip.MarkVendorStarted(vehicleId: null, vendorVehicleKey: vehKey);
                    logger.LogInformation("[EnvelopeWebhook] Trip {TripId} started (upperKey {UpperKey}, vendor vehicle '{VehKey}')",
                        trip.Id, upperKey, vehKey ?? "(none)");
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
                    // Vendor paused the order (obstacle, traffic stop, admin
                    // hold, etc). Mirror to Trip.Paused so operators see the
                    // real state and don't issue a duplicate Pause command.
                    var hangReason = payload.Task?.HangReason;
                    trip.Pause();
                    logger.LogInformation("[EnvelopeWebhook] Trip {TripId} paused by vendor (upperKey {UpperKey}) event={Event} reason={Reason}",
                        trip.Id, upperKey, eventType, hangReason ?? "(none)");
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
        AMR.DeliveryPlanning.Facility.Application.Services.IFacilityReadService facilityReadService,
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

        // Sub-task events carry the parent task's upperKey on payload.task,
        // not payload.subTask. Need it to find the owning Trip.
        var upperKey = payload.Task?.UpperKey;
        if (string.IsNullOrWhiteSpace(upperKey))
        {
            logger.LogDebug("RIOT3 sub-task event {Event} for {SubTaskKey} has no parent upperKey — cannot correlate to a Trip",
                eventType, subTaskKey);
            return;
        }

        var trip = await tripRepository.GetByUpperKeyAsync(upperKey, cancellationToken);
        if (trip is null)
        {
            logger.LogWarning("[SubTaskWebhook] No Trip found for upperKey {UpperKey} (subTask {SubTaskKey}, event {Event}) — ignored.",
                upperKey, subTaskKey, eventType);
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
            logger.LogInformation("[SubTaskWebhook] Trip {TripId} mission {MissionKey} → {State}",
                trip.Id, subTaskKey, state);
        else
            logger.LogDebug("[SubTaskWebhook] Trip {TripId} mission {MissionKey} {State} — duplicate, skipped",
                trip.Id, subTaskKey, state);

        // ── Item-Picked / DroppedOff detection ─────────────────────────
        // Once an ACT (pickup/drop action) finishes at the trip's
        // pickup OR drop station, fire the matching domain event so the
        // DeliveryOrder side flips item status. Ignored when:
        //   • state != FINISHED         (only completion counts)
        //   • mission type != ACT       (MOVE arrivals don't mean loaded)
        //   • duplicate webhook         (already-stored row, no event)
        //   • trip has no pickup/drop   (pre-Gap-3 trip — degrade silently)
        //   • station code resolves to neither pickup nor drop
        if (state == "FINISHED" && inserted)
        {
            var missionType = string.IsNullOrWhiteSpace(subTask.SubTaskType) ? "" : subTask.SubTaskType.ToUpperInvariant();
            if (missionType == "ACT" && !string.IsNullOrWhiteSpace(stationName))
            {
                var resolvedId = await facilityReadService.ResolveStationByCodeAsync(stationName, cancellationToken);
                var pickupHit = trip.PickupStationId.HasValue && resolvedId == trip.PickupStationId.Value;
                var dropHit   = trip.DropStationId.HasValue   && resolvedId == trip.DropStationId.Value;
                if (pickupHit)
                {
                    trip.MarkVendorPickedUp();
                    await tripRepository.UpdateAsync(trip, cancellationToken);
                    logger.LogInformation(
                        "[SubTaskWebhook] Trip {TripId} pickup completed at {Station} — items will be marked Picked",
                        trip.Id, stationName);
                }
                else if (dropHit)
                {
                    trip.MarkVendorDropCompleted();
                    await tripRepository.UpdateAsync(trip, cancellationToken);
                    logger.LogInformation(
                        "[SubTaskWebhook] Trip {TripId} drop completed at {Station} — items will be marked DroppedOff",
                        trip.Id, stationName);
                }
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

    private static string MapRiotState(string state) => state.ToLower() switch
    {
        "idle" => "Idle",
        "running" => "Moving",
        "error" => "Error",
        "charging" => "Charging",
        "working" => "Working",
        _ => "Offline"
    };

    private static string MapRiotSystemState(string? systemState) => systemState?.ToUpper() switch
    {
        "IDLE" => "Idle",
        "BUSY" or "RUNNING" or "EXECUTING" => "Moving",
        "ERROR" => "Error",
        "CHARGING" => "Charging",
        _ => "Offline"
    };
}

public class Riot3ActionCallbackPayload
{
    public string? TaskId { get; set; }
    public string? Result { get; set; }
    public string? ErrorCode { get; set; }
}
