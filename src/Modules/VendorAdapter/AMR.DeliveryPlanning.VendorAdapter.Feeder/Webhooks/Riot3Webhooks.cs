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

        // Full RIOT3.0 /api/v4/notify endpoint
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
        if (!Guid.TryParse(payload.UpperKey, out var taskId)) return;

        switch (payload.TaskEventType?.ToLower())
        {
            case "finished":
                logger.LogInformation("RIOT3 task finished: {TaskId}", taskId);
                await outbox.AddAsync(new Riot3TaskCompletedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, taskId, payload.OrderKey ?? string.Empty), cancellationToken);
                break;

            case "failed":
                var errorCode = payload.FailResult?.ErrorCode ?? "UNKNOWN";
                var errorMsg = payload.FailResult?.ErrorMsg ?? "Task failed";
                logger.LogWarning("RIOT3 task failed: {TaskId} [{Code}] {Msg}", taskId, errorCode, errorMsg);
                await outbox.AddAsync(new Riot3TaskFailedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, taskId, payload.OrderKey ?? string.Empty, errorCode, errorMsg), cancellationToken);
                break;

            case "started":
                logger.LogDebug("RIOT3 task started: {TaskId}", taskId);
                break;

            case "progress":
                logger.LogDebug("RIOT3 task progress: {TaskId} {Pct}%", taskId, payload.Progress);
                break;
        }
    }

    private static async Task HandleSubTaskEvent(
        Riot3NotifyPayload payload,
        IVendorAdapterOutbox outbox,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(payload.TaskId, out var subTaskId)) return;

        switch (payload.TaskEventType?.ToLower())
        {
            case "finished":
                await outbox.AddAsync(new Riot3TaskCompletedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, subTaskId, payload.OrderKey ?? string.Empty), cancellationToken);
                break;

            case "failed":
                var code = payload.FailResult?.ErrorCode ?? "UNKNOWN";
                var msg = payload.FailResult?.ErrorMsg ?? "SubTask failed";
                await outbox.AddAsync(new Riot3TaskFailedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, subTaskId, payload.OrderKey ?? string.Empty, code, msg), cancellationToken);
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
        var vehicle = payload.Vehicle;
        if (vehicle == null || string.IsNullOrWhiteSpace(vehicle.DeviceKey)) return;

        var vehicleId = await vehicleIdentityResolver.ResolveVehicleIdAsync("riot3", vehicle.DeviceKey, cancellationToken);
        if (!vehicleId.HasValue)
        {
            logger.LogWarning("RIOT3 vehicle event ignored because deviceKey {DeviceKey} is not mapped", vehicle.DeviceKey);
            return;
        }

        var canonicalState = MapRiotSystemState(vehicle.SystemState);
        var batteryPct = vehicle.BatteryLevel / 100.0;

        await outbox.AddAsync(new VehicleStateChangedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, vehicleId.Value, canonicalState, batteryPct, null), cancellationToken);

        if (payload.VehicleEventType?.ToLower() == "emergency_triggered" ||
            vehicle.SafetyState?.Contains("EMERGENCY") == true)
        {
            logger.LogWarning("RIOT3 emergency triggered for vehicle {VehicleId}", vehicleId.Value);
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
