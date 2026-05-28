using System.Net.Http.Json;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;

public class Riot3CommandService : IVehicleCommandService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<Riot3CommandService> _logger;

    public Riot3CommandService(HttpClient httpClient, ILogger<Riot3CommandService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Result> SendTaskAsync(Guid vehicleId, RobotTaskCommand command, CancellationToken cancellationToken = default)
    {
        var riot3VehicleKey = command.VendorVehicleKey;

        if (string.IsNullOrWhiteSpace(riot3VehicleKey))
            _logger.LogInformation("Sending task {TaskId} ({Action}) to RIOT3 without AppointVehicleKey — RIOT3 will auto-assign",
                command.TaskId, command.Action);
        else
            _logger.LogInformation("Sending task {TaskId} ({Action}) to RIOT3 vehicle {VehicleId} ({DeviceKey})",
                command.TaskId, command.Action, vehicleId, riot3VehicleKey);

        if (RequiresStationTarget(command)
            && (string.IsNullOrWhiteSpace(command.MapId) || string.IsNullOrWhiteSpace(command.TargetNodeId)))
        {
            _logger.LogWarning(
                "RIOT3 task {TaskId} ({Action}) has incomplete target: MapId={MapId}, TargetNodeId={TargetNodeId}",
                command.TaskId, command.Action, command.MapId, command.TargetNodeId);
            return Result.Failure("RIOT3 MOVE/CHARGE task requires map vendor ref and station vendor ref.");
        }

        var mission = BuildMission(command);
        if (mission is null)
        {
            return Result.Failure($"RIOT3 task {command.TaskId}: failed to build mission for action {command.Action}.");
        }

        var request = new Riot3OrderRequest
        {
            UpperKey = command.TaskId.ToString(),
            OrderName = $"Task-{command.TaskId}",
            OrderType = "WORK",
            Priority = 10,
            StructureType = "sequence",
            AppointVehicleKey = string.IsNullOrWhiteSpace(riot3VehicleKey) ? null : riot3VehicleKey,
            Missions = new List<Riot3Mission> { mission }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v4/orders", request, cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("RIOT3 order accepted for task {TaskId}", command.TaskId);
            return Result.Success();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to send task {TaskId} to RIOT3", command.TaskId);
            return Result.Failure($"RIOT3 API error: {ex.Message}");
        }
    }

    // Multi-mission send used by the OrderTemplate instantiate flow. Bypasses
    // the per-task `SendTaskAsync` shape because the caller has already built
    // the full RIOT3 envelope (multiple missions, vendor-binding hints, etc).
    // Returns the orderKey RIOT3 minted for the new order on success.
    public async Task<Result<string>> SendOrderAsync(
        Riot3OrderRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Sending RIOT3 order upperKey={UpperKey} with {Count} missions (structureType={Structure})",
            request.UpperKey, request.Missions.Count, request.StructureType);

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v4/orders", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            // RIOT3 returns the created order; parse out the orderKey so the
            // caller can correlate later callbacks.
            var payload = await response.Content.ReadFromJsonAsync<Riot3CreateOrderResponse>(cancellationToken);
            var orderKey = payload?.Data?.OrderKey ?? string.Empty;
            _logger.LogInformation("RIOT3 accepted order upperKey={UpperKey} orderKey={OrderKey}",
                request.UpperKey, orderKey);
            return Result<string>.Success(orderKey);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to send RIOT3 order upperKey={UpperKey}", request.UpperKey);
            return Result<string>.Failure($"RIOT3 API error: {ex.Message}");
        }
    }

    public Task<Result> CancelTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
        => SendOrderOperationAsync(taskId, Riot3OrderCommandType.Cancel, "cancel", cancellationToken);

    public Task<Result> PauseTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
        => SendOrderOperationAsync(taskId, Riot3OrderCommandType.Hold, "pause", cancellationToken);

    public Task<Result> ResumeTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
        // We pause via CMD_ORDER_HELD, so the inverse is CMD_ORDER_CONTINUE_FROM_HELD.
        // CMD_ORDER_CONTINUE_FROM_HANG is for system-initiated hangs (e.g. traffic stops).
        => SendOrderOperationAsync(taskId, Riot3OrderCommandType.ContinueFromHeld, "resume", cancellationToken);

    public async Task<StandardRobotState?> GetVehicleStateAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<Riot3VehicleResponse>(
                $"/api/v4/robots/{vehicleId}",
                cancellationToken);

            if (response == null) return null;

            return new StandardRobotState
            {
                VehicleId = vehicleId,
                State = MapSystemState(response.SystemState),
                BatteryLevel = response.BatteryLevel / 100.0,
                CurrentX = response.Position?.X,
                CurrentY = response.Position?.Y,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to get vehicle state for {VehicleId} from RIOT3", vehicleId);
            return null;
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<Result> SendOrderOperationAsync(
        Guid taskId,
        string orderCommandType,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("RIOT3 order operation {Operation} ({Command}) for task {TaskId}",
            operationLabel, orderCommandType, taskId);

        var envelope = new Riot3OrderOperationEnvelope
        {
            OrderCommand = new Riot3OrderOperationRequest
            {
                OrderCommandType = orderCommandType,
                DisableVehicle = false
            }
        };

        try
        {
            var upperKey = taskId.ToString();
            var response = await _httpClient.PutAsJsonAsync(
                $"/api/v4/orders/{upperKey}/operation?isUpper=true",
                envelope,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return Result.Success();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to {Operation} RIOT3 order for task {TaskId}", operationLabel, taskId);
            return Result.Failure($"RIOT3 {operationLabel} error: {ex.Message}");
        }
    }

    private static bool RequiresStationTarget(RobotTaskCommand command)
        => command.Action is RobotActionType.MOVE or RobotActionType.CHARGE;

    // RIOT3 standardRobotsCustom action — the lift/drop encoding the codebase
    // has been using since this adapter was first wired. Verified against
    // RIOT3 spec example (page 6): actionParameters carry id + param0/1.
    private const string StandardRobotsCustomAction = "standardRobotsCustom";
    private const string StandardRobotsCustomActionId = "4";

    private Riot3Mission? BuildMission(RobotTaskCommand command)
    {
        var mission = new Riot3Mission
        {
            MissionKey = command.TaskId.ToString(),
            ActionName = $"{command.Action}-{command.TaskId}",
            Category = "agv",
            BlockingType = "NONE"
        };

        switch (command.Action)
        {
            case RobotActionType.MOVE:
            case RobotActionType.CHARGE:
                mission.Type = "MOVE";
                if (!TryAssignMoveTarget(mission, command))
                    return null;
                break;

            case RobotActionType.LIFT:
                mission.Type = "ACT";
                mission.ActionType = StandardRobotsCustomAction;
                mission.ActionParameters = new List<Riot3ActionParam>
                {
                    new() { Key = "id", Value = StandardRobotsCustomActionId },
                    new() { Key = "param0", Value = "1" },
                    new() { Key = "param1", Value = "0" }
                };
                break;

            case RobotActionType.DROP:
                mission.Type = "ACT";
                mission.ActionType = StandardRobotsCustomAction;
                mission.ActionParameters = new List<Riot3ActionParam>
                {
                    new() { Key = "id", Value = StandardRobotsCustomActionId },
                    new() { Key = "param0", Value = "2" },
                    new() { Key = "param1", Value = "0" }
                };
                break;

            case RobotActionType.ACT:
                mission.Type = "ACT";
                if (command.AdditionalParameters != null)
                {
                    if (command.AdditionalParameters.TryGetValue("actionType", out var actionTypeStr))
                        mission.ActionType = actionTypeStr;

                    mission.ActionParameters = command.AdditionalParameters
                        .Where(kv => kv.Key != "actionType")
                        .Select(kv => new Riot3ActionParam { Key = kv.Key, Value = kv.Value })
                        .ToList();
                }
                break;

            case RobotActionType.PARK:
            case RobotActionType.WAIT:
            default:
                mission.Type = "MOVE";
                if (!TryAssignMoveTarget(mission, command))
                    return null;
                break;
        }

        return mission;
    }

    private bool TryAssignMoveTarget(Riot3Mission mission, RobotTaskCommand command)
    {
        if (!int.TryParse(command.MapId, out var mapId))
        {
            _logger.LogError("RIOT3 task {TaskId}: MapId '{MapId}' is not a valid integer",
                command.TaskId, command.MapId);
            return false;
        }

        if (!int.TryParse(command.TargetNodeId, out var stationId))
        {
            _logger.LogError("RIOT3 task {TaskId}: TargetNodeId '{NodeId}' is not a valid integer",
                command.TaskId, command.TargetNodeId);
            return false;
        }

        mission.MapId = mapId;
        mission.StationId = stationId;
        return true;
    }

    private static StandardState MapSystemState(string systemState) => systemState?.ToUpperInvariant() switch
    {
        "IDLE" => StandardState.Idle,
        "BUSY" => StandardState.Moving,
        "ERROR" => StandardState.Error,
        "CHARGING" => StandardState.Charging,
        "MAINTENANCE" => StandardState.Maintenance,
        _ => StandardState.Offline
    };
}
