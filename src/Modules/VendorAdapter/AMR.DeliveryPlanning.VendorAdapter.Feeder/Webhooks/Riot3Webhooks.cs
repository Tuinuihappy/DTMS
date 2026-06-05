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
                    await HandleSubTaskEvent(payload, outbox, logger, cancellationToken);
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
                    // RIOT3 may also report the chosen robot here via
                    // processingVehicle.key — propagate if present.
                    Guid? vehicleId = null;
                    var vehKey = payload.Task?.ProcessingVehicle?.Key;
                    if (Guid.TryParse(vehKey, out var v)) vehicleId = v;
                    trip.MarkVendorStarted(vehicleId);
                    logger.LogInformation("[EnvelopeWebhook] Trip {TripId} started (upperKey {UpperKey}, vehicle {VehicleId})",
                        trip.Id, upperKey, vehicleId?.ToString() ?? "(unassigned)");
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

    private static async Task HandleSubTaskEvent(
        Riot3NotifyPayload payload,
        IVendorAdapterOutbox outbox,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Envelope flow consumes only the parent task lifecycle (see
        // HandleEnvelopeTaskEvent). Sub-task events are useful for ops
        // visibility but no longer drive aggregate state. Log and move on.
        var subTaskKey = payload.SubTask?.Key;
        var eventType = payload.TaskEventType?.ToUpperInvariant();
        if (eventType is "SUB_TASK_FAILED")
        {
            var failResult = payload.SubTask?.FailResult;
            logger.LogWarning("RIOT3 sub-task {SubTaskKey} failed: [{Code}] {Msg}",
                subTaskKey, failResult?.ErrorCode ?? "UNKNOWN", failResult?.ErrorDescription ?? "(no description)");
        }
        else
        {
            logger.LogDebug("RIOT3 sub-task event {Event} for {SubTaskKey}", eventType, subTaskKey);
        }
        await Task.CompletedTask;
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
