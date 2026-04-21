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
        _logger.LogInformation("Sending task {TaskId} to Riot3 robot {VehicleId}", command.TaskId, vehicleId);

        var request = new RiotTaskRequest
        {
            TaskId = command.TaskId.ToString(),
            ActionType = command.Action.ToString(), // MOVE, LIFT, DROP, CHARGE
            Destination = command.TargetNodeId,
            Params = command.AdditionalParameters
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/v1/robots/{vehicleId}/tasks", request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send task to Riot3 robot {VehicleId}", vehicleId);
            return Result.Failure($"Failed to communicate with Riot3 API: {ex.Message}");
        }
    }

    public async Task<Result> CancelTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cancelling task {TaskId} for Riot3 robot {VehicleId}", taskId, vehicleId);
        
        try
        {
            var response = await _httpClient.PostAsync($"/api/v1/robots/{vehicleId}/tasks/{taskId}/cancel", null, cancellationToken);
            response.EnsureSuccessStatusCode();
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel task for Riot3 robot {VehicleId}", vehicleId);
            return Result.Failure($"Failed to cancel Riot3 task: {ex.Message}");
        }
    }

    public Task<Result> PauseTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        // Implementation for Pause
        return Task.FromResult(Result.Success());
    }

    public Task<Result> ResumeTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        // Implementation for Resume
        return Task.FromResult(Result.Success());
    }
}
