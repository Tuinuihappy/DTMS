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
        {
            _logger.LogWarning("RIOT3 vehicle {VehicleId} has no VendorVehicleKey/deviceKey; task {TaskId} was not sent",
                vehicleId, command.TaskId);
            return Result.Failure($"RIOT3 vehicle {vehicleId} has no configured VendorVehicleKey/deviceKey.");
        }

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
        var request = new Riot3OrderRequest
        {
            UpperKey = command.TaskId.ToString(),
            OrderName = $"Task-{command.TaskId}",
            OrderType = "WORK",
            Priority = 10,
            StructureType = "sequence",
            AppointVehicleKey = riot3VehicleKey,
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

    public async Task<Result> CancelTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cancelling task {TaskId} via RIOT3 order cancel", taskId);

        var operationRequest = new Riot3OrderOperationRequest { OrderCommandType = Riot3OrderCommandType.Cancel };

        try
        {
            var upperKey = taskId.ToString();
            var response = await _httpClient.PutAsJsonAsync(
                $"/api/v4/orders/{upperKey}/operation?isUpper=true",
                operationRequest,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return Result.Success();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to cancel RIOT3 order for task {TaskId}", taskId);
            return Result.Failure($"RIOT3 cancel error: {ex.Message}");
        }
    }

    public async Task<Result> PauseTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Pausing task {TaskId} via RIOT3 order hold", taskId);

        var operationRequest = new Riot3OrderOperationRequest { OrderCommandType = Riot3OrderCommandType.Hold };

        try
        {
            var upperKey = taskId.ToString();
            var response = await _httpClient.PutAsJsonAsync(
                $"/api/v4/orders/{upperKey}/operation?isUpper=true",
                operationRequest,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return Result.Success();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to pause RIOT3 order for task {TaskId}", taskId);
            return Result.Failure($"RIOT3 pause error: {ex.Message}");
        }
    }

    public async Task<Result> ResumeTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resuming task {TaskId} via RIOT3 order resume", taskId);

        var operationRequest = new Riot3OrderOperationRequest { OrderCommandType = Riot3OrderCommandType.Resume };

        try
        {
            var upperKey = taskId.ToString();
            var response = await _httpClient.PutAsJsonAsync(
                $"/api/v4/orders/{upperKey}/operation?isUpper=true",
                operationRequest,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return Result.Success();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to resume RIOT3 order for task {TaskId}", taskId);
            return Result.Failure($"RIOT3 resume error: {ex.Message}");
        }
    }

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

    private static bool RequiresStationTarget(RobotTaskCommand command)
        => command.Action is RobotActionType.MOVE or RobotActionType.CHARGE;

    private static Riot3Mission BuildMission(RobotTaskCommand command)
    {
        var mission = new Riot3Mission
        {
            MissionId = command.TaskId.ToString(),
            MissionName = $"{command.Action}-{command.TaskId}",
            BlockingType = "HARD"
        };

        switch (command.Action)
        {
            case RobotActionType.MOVE:
                mission.Type = "MOVE";
                mission.MapId = command.MapId;
                mission.StationId = command.TargetNodeId;
                break;

            case RobotActionType.LIFT:
                mission.Type = "ACT";
                mission.ActionType = 4;
                mission.Parameters = new List<Riot3ActionParam>
                {
                    new() { Key = "0", Value = "1" },
                    new() { Key = "1", Value = "0" }
                };
                break;

            case RobotActionType.DROP:
                mission.Type = "ACT";
                mission.ActionType = 4;
                mission.Parameters = new List<Riot3ActionParam>
                {
                    new() { Key = "0", Value = "2" },
                    new() { Key = "1", Value = "0" }
                };
                break;

            case RobotActionType.CHARGE:
                mission.Type = "MOVE";
                mission.MapId = command.MapId;
                mission.StationId = command.TargetNodeId;
                break;

            case RobotActionType.ACT:
                mission.Type = "ACT";
                if (command.AdditionalParameters != null)
                {
                    if (command.AdditionalParameters.TryGetValue("actionType", out var actionTypeStr)
                        && int.TryParse(actionTypeStr, out var actionType))
                        mission.ActionType = actionType;

                    mission.Parameters = command.AdditionalParameters
                        .Where(kv => kv.Key != "actionType")
                        .Select(kv => new Riot3ActionParam { Key = kv.Key, Value = kv.Value })
                        .ToList();
                }
                break;

            default:
                mission.Type = "MOVE";
                mission.MapId = command.MapId;
                mission.StationId = command.TargetNodeId;
                break;
        }

        return mission;
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
