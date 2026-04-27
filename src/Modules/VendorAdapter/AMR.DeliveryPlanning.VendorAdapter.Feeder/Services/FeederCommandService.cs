using System.Net.Http.Json;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Feeder.Services;

public class FeederCommandService : IVehicleCommandService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FeederCommandService> _logger;

    public FeederCommandService(HttpClient httpClient, ILogger<FeederCommandService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Result> SendTaskAsync(Guid vehicleId, RobotTaskCommand command, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Feeder: Sending task {TaskId} ({Action}) to vehicle {VehicleId}", command.TaskId, command.Action, vehicleId);

        if (command.Action == RobotActionType.MOVE)
        {
            var moveReq = new { destination = command.TargetNodeId, vehicleId = vehicleId.ToString() };
            var response = await _httpClient.PostAsJsonAsync("/api/feeder/move", moveReq, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Result.Failure($"Feeder MOVE failed: {response.StatusCode}");
            return Result.Success();
        }

        var (program1, program2, program3) = MapToProgram(command.Action, command.AdditionalParameters);
        var request = new FeederProgramRequest
        {
            TaskId = command.TaskId.ToString(),
            VehicleId = vehicleId.ToString(),
            Program1 = program1,
            Program2 = program2,
            Program3 = program3
        };

        try
        {
            var res = await _httpClient.PostAsJsonAsync("/api/feeder/program", request, cancellationToken);
            res.EnsureSuccessStatusCode();
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Feeder: Failed to send program to vehicle {VehicleId}", vehicleId);
            return Result.Failure($"Feeder API error: {ex.Message}");
        }
    }

    public async Task<Result> CancelTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpClient.PostAsJsonAsync($"/api/feeder/cancel", new { vehicleId = vehicleId.ToString(), taskId = taskId.ToString() }, cancellationToken);
            res.EnsureSuccessStatusCode();
            return Result.Success();
        }
        catch (Exception ex) { return Result.Failure(ex.Message); }
    }

    public async Task<Result> PauseTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpClient.PostAsJsonAsync($"/api/feeder/pause", new { vehicleId = vehicleId.ToString() }, cancellationToken);
            res.EnsureSuccessStatusCode();
            return Result.Success();
        }
        catch (Exception ex) { return Result.Failure(ex.Message); }
    }

    public async Task<Result> ResumeTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpClient.PostAsJsonAsync($"/api/feeder/resume", new { vehicleId = vehicleId.ToString() }, cancellationToken);
            res.EnsureSuccessStatusCode();
            return Result.Success();
        }
        catch (Exception ex) { return Result.Failure(ex.Message); }
    }

    public Task<StandardRobotState?> GetVehicleStateAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        // Feeder AMRs use a simple polling endpoint
        return Task.FromResult<StandardRobotState?>(new StandardRobotState
        {
            VehicleId = vehicleId,
            State = StandardState.Idle,
            BatteryLevel = 1.0,
            Timestamp = DateTime.UtcNow
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Action catalog: canonical action → program triplet
    // Based on design spec (Image 1): program1=192 fixed for all operations
    // ──────────────────────────────────────────────────────────────────────────
    private static (int p1, int p2, int p3) MapToProgram(RobotActionType action, Dictionary<string, string>? extra)
    {
        // Check for override from AdditionalParameters (data-driven catalog)
        if (extra != null &&
            extra.TryGetValue("program1", out var p1s) && int.TryParse(p1s, out var p1) &&
            extra.TryGetValue("program2", out var p2s) && int.TryParse(p2s, out var p2) &&
            extra.TryGetValue("program3", out var p3s) && int.TryParse(p3s, out var p3))
            return (p1, p2, p3);

        return action switch
        {
            RobotActionType.LIFT   => (192, 1, 3),   // LEFT_SIDE_LOAD
            RobotActionType.DROP   => (192, 1, 4),   // LEFT_SIDE_UNLOAD
            RobotActionType.CHARGE => (192, 100, 100), // INIT / return to charge
            RobotActionType.ACT when extra?.GetValueOrDefault("side") == "right" && extra?.GetValueOrDefault("op") == "load"   => (192, 2, 3),
            RobotActionType.ACT when extra?.GetValueOrDefault("side") == "right" && extra?.GetValueOrDefault("op") == "unload" => (192, 2, 4),
            _                      => (192, 100, 100)  // INIT fallback
        };
    }
}

public class FeederProgramRequest
{
    public string TaskId { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public int Program1 { get; set; }
    public int Program2 { get; set; }
    public int Program3 { get; set; }
}
