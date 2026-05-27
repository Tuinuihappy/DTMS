using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.Fleet.IntegrationEvents;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Feeder.Webhooks;

public static class Riot3Webhooks
{
    public static void MapRiot3Webhooks(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/webhooks/riot3").WithTags("Webhooks");

        // RIOT3.0 v4 /api/v4/notify callback — task / subTask / vehicle events
        group.MapPost("/notify", async (
            Riot3NotifyPayload payload,
            IVendorAdapterOutbox outbox,
            IVehicleIdentityResolver vehicleIdentityResolver,
            ILogger<Riot3NotifyPayload> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogDebug("RIOT3 notify: type={Type} taskEvent={TaskEvent} vehicleEvent={VehicleEvent}",
                payload.Type, payload.TaskEventType, payload.VehicleEventType);

            switch (NormalizeNotifyType(payload.Type))
            {
                case "task":
                    await HandleTaskEvent(payload, outbox, logger, cancellationToken);
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
        });

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
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // upperKey = our TaskId (used as idempotency key when submitting)
        var upperKey = payload.Task?.UpperKey;
        if (!Guid.TryParse(upperKey, out var taskId))
        {
            logger.LogWarning("RIOT3 task event missing/invalid upperKey: {UpperKey}", upperKey);
            return;
        }

        var orderKey = payload.Task?.Key ?? string.Empty;

        switch (payload.TaskEventType?.ToUpperInvariant())
        {
            case "TASK_FINISHED":
                logger.LogInformation("RIOT3 task finished: {TaskId}", taskId);
                await outbox.AddAsync(new Riot3TaskCompletedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, taskId, orderKey), cancellationToken);
                break;

            case "TASK_FAILED":
                var failResult = payload.Task?.FailReason;
                var errorCode = failResult?.ErrorCode ?? "UNKNOWN";
                var errorMsg = failResult?.ErrorDescription ?? "Task failed";
                logger.LogWarning("RIOT3 task failed: {TaskId} [{Code}] {Msg}", taskId, errorCode, errorMsg);
                await outbox.AddAsync(new Riot3TaskFailedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, taskId, orderKey, errorCode, errorMsg), cancellationToken);
                break;

            case "TASK_CANCELED":
                logger.LogInformation("RIOT3 task canceled: {TaskId} reason={Reason}",
                    taskId, payload.Task?.CancelReason);
                break;

            case "TASK_PROCESSING":
                logger.LogDebug("RIOT3 task started: {TaskId}", taskId);
                break;

            case "TASK_HANG":
            case "TASK_HELD":
                logger.LogInformation("RIOT3 task paused: {TaskId} event={Event} reason={Reason}",
                    taskId, payload.TaskEventType, payload.Task?.HangReason);
                break;

            case "TASK_HANG_TO_CONTINUE":
            case "TASK_HELD_TO_CONTINUE":
                logger.LogDebug("RIOT3 task resumed: {TaskId} event={Event}",
                    taskId, payload.TaskEventType);
                break;
        }
    }

    private static async Task HandleSubTaskEvent(
        Riot3NotifyPayload payload,
        IVendorAdapterOutbox outbox,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Sub-tasks carry their own RIOT-side key; the parent order's
        // upperKey lives on the task object alongside.
        var subTaskKey = payload.SubTask?.Key;
        if (!Guid.TryParse(subTaskKey, out var subTaskId))
        {
            logger.LogDebug("RIOT3 sub-task event missing/invalid key: {Key}", subTaskKey);
            return;
        }

        var orderKey = payload.Task?.Key ?? payload.SubTask?.TaskKey ?? string.Empty;

        switch (payload.TaskEventType?.ToUpperInvariant())
        {
            case "SUB_TASK_FINISHED":
                await outbox.AddAsync(new Riot3TaskCompletedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, subTaskId, orderKey), cancellationToken);
                break;

            case "SUB_TASK_FAILED":
                var failResult = payload.SubTask?.FailResult;
                var code = failResult?.ErrorCode ?? "UNKNOWN";
                var msg = failResult?.ErrorDescription ?? "SubTask failed";
                await outbox.AddAsync(new Riot3TaskFailedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, subTaskId, orderKey, code, msg), cancellationToken);
                break;

            case "SUB_TASK_CANCELED":
                logger.LogInformation("RIOT3 sub-task canceled: {SubTaskId}", subTaskId);
                break;

            case "SUB_TASK_PROCESSING":
                logger.LogDebug("RIOT3 sub-task started: {SubTaskId}", subTaskId);
                break;
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
