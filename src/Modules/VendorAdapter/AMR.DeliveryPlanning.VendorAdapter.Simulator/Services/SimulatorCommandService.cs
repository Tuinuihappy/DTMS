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
