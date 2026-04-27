using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Simulator.Services;

public class SimulatorCommandService : IVehicleCommandService
{
    private readonly ILogger<SimulatorCommandService> _logger;

    public SimulatorCommandService(ILogger<SimulatorCommandService> logger)
    {
        _logger = logger;
    }

    public async Task<Result> SendTaskAsync(Guid vehicleId, RobotTaskCommand command, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Simulator: Sending task {TaskId} ({Action}) to vehicle {VehicleId}", command.TaskId, command.Action, vehicleId);

        // Simulate network delay
        await Task.Delay(500, cancellationToken);

        _logger.LogInformation("Simulator: Task {TaskId} accepted by vehicle {VehicleId}", command.TaskId, vehicleId);
        
        // In a real simulator, this would spin up a background task to simulate progress 
        // and eventually call a webhook or publish an event to signify completion.
        
        return Result.Success();
    }

    public async Task<Result> CancelTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Simulator: Cancelling task {TaskId} for vehicle {VehicleId}", taskId, vehicleId);
        await Task.Delay(200, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> PauseTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Simulator: Pausing task {TaskId} for vehicle {VehicleId}", taskId, vehicleId);
        await Task.Delay(200, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResumeTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Simulator: Resuming task {TaskId} for vehicle {VehicleId}", taskId, vehicleId);
        await Task.Delay(200, cancellationToken);
        return Result.Success();
    }

    public Task<StandardRobotState?> GetVehicleStateAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Simulator: Getting state for vehicle {VehicleId}", vehicleId);
        var state = new StandardRobotState
        {
            VehicleId = vehicleId,
            State = StandardState.Idle,
            BatteryLevel = 0.85,
            CurrentX = 0,
            CurrentY = 0,
            Timestamp = DateTime.UtcNow
        };
        return Task.FromResult<StandardRobotState?>(state);
    }
}
