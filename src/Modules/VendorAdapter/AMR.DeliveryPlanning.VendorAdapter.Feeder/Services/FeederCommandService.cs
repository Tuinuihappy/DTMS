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

    public Task<StandardRobotState?> GetVehicleStateAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        // Feeder AMRs use a simple polling endpoint; for now we return a
        // synthetic Idle reading so the factory has something to return.
        return Task.FromResult<StandardRobotState?>(new StandardRobotState
        {
            VehicleId = vehicleId,
            State = StandardState.Idle,
            BatteryLevel = 1.0,
            Timestamp = DateTime.UtcNow
        });
    }
}
