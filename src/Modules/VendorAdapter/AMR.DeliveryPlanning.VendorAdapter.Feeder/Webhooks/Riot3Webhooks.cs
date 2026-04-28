using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.Fleet.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
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
        group.MapPost("/notify", async (Riot3NotifyPayload payload, IEventBus eventBus, ILogger<Riot3NotifyPayload> logger) =>
        {
            logger.LogDebug("RIOT3 notify: type={Type} taskEvent={TaskEvent} vehicleEvent={VehicleEvent}",
                payload.Type, payload.TaskEventType, payload.VehicleEventType);

            switch (payload.Type?.ToLower())
            {
                case "task":
                    await HandleTaskEvent(payload, eventBus, logger);
                    break;

                case "subtask":
                    await HandleSubTaskEvent(payload, eventBus, logger);
                    break;

                case "vehicle":
                    await HandleVehicleEvent(payload, eventBus, logger);
                    break;

                default:
                    logger.LogWarning("Unknown RIOT3 notify type: {Type}", payload.Type);
                    break;
            }

            return Results.Ok();
        });

        // Legacy simple status endpoint (kept for backward compatibility)
        group.MapPost("/status", async (RiotStatusPayload payload, IEventBus eventBus, ILogger<RiotStatusPayload> logger) =>
        {
            if (!Guid.TryParse(payload.RobotId, out var vehicleId))
                return Results.BadRequest("Invalid RobotId format.");

            await eventBus.PublishAsync(new VehicleStateChangedIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, vehicleId,
                MapRiotState(payload.State), payload.Battery,
                payload.CurrentNode != null && Guid.TryParse(payload.CurrentNode, out var nodeId) ? nodeId : null));

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

    private static async Task HandleTaskEvent(Riot3NotifyPayload payload, IEventBus eventBus, ILogger logger)
    {
        // upperKey = our TaskId (used as idempotency key when submitting)
        if (!Guid.TryParse(payload.UpperKey, out var taskId)) return;

        switch (payload.TaskEventType?.ToLower())
        {
            case "finished":
                logger.LogInformation("RIOT3 task finished: {TaskId}", taskId);
                await eventBus.PublishAsync(new Riot3TaskCompletedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, taskId, payload.OrderKey ?? string.Empty));
                break;

            case "failed":
                var errorCode = payload.FailResult?.ErrorCode ?? "UNKNOWN";
                var errorMsg = payload.FailResult?.ErrorMsg ?? "Task failed";
                logger.LogWarning("RIOT3 task failed: {TaskId} [{Code}] {Msg}", taskId, errorCode, errorMsg);
                await eventBus.PublishAsync(new Riot3TaskFailedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, taskId, payload.OrderKey ?? string.Empty, errorCode, errorMsg));
                break;

            case "started":
                logger.LogDebug("RIOT3 task started: {TaskId}", taskId);
                break;

            case "progress":
                logger.LogDebug("RIOT3 task progress: {TaskId} {Pct}%", taskId, payload.Progress);
                break;
        }
    }

    private static async Task HandleSubTaskEvent(Riot3NotifyPayload payload, IEventBus eventBus, ILogger logger)
    {
        if (!Guid.TryParse(payload.TaskId, out var subTaskId)) return;

        switch (payload.TaskEventType?.ToLower())
        {
            case "finished":
                await eventBus.PublishAsync(new Riot3TaskCompletedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, subTaskId, payload.OrderKey ?? string.Empty));
                break;

            case "failed":
                var code = payload.FailResult?.ErrorCode ?? "UNKNOWN";
                var msg = payload.FailResult?.ErrorMsg ?? "SubTask failed";
                await eventBus.PublishAsync(new Riot3TaskFailedIntegrationEvent(
                    Guid.NewGuid(), DateTime.UtcNow, subTaskId, payload.OrderKey ?? string.Empty, code, msg));
                break;
        }
    }

    private static async Task HandleVehicleEvent(Riot3NotifyPayload payload, IEventBus eventBus, ILogger logger)
    {
        var vehicle = payload.Vehicle;
        if (vehicle == null || !Guid.TryParse(vehicle.DeviceKey, out var vehicleId)) return;

        var canonicalState = MapRiotSystemState(vehicle.SystemState);
        var batteryPct = vehicle.BatteryLevel / 100.0;

        await eventBus.PublishAsync(new VehicleStateChangedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, vehicleId, canonicalState, batteryPct, null));

        if (payload.VehicleEventType?.ToLower() == "emergency_triggered" ||
            vehicle.SafetyState?.Contains("EMERGENCY") == true)
        {
            logger.LogWarning("RIOT3 emergency triggered for vehicle {VehicleId}", vehicleId);
        }

        if (batteryPct < 0.20)
        {
            await eventBus.PublishAsync(new VehicleBatteryLowIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, vehicleId, Guid.Empty, batteryPct));
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
        "BUSY" => "Moving",
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
