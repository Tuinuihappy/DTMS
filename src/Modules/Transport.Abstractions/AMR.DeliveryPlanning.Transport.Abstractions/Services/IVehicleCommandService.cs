using AMR.DeliveryPlanning.Transport.Abstractions.Models;

namespace AMR.DeliveryPlanning.Transport.Abstractions.Services;

// Per-task command surface removed in Phase b7 — envelope dispatch
// delegates execution to the vendor; DTMS no longer schedules individual
// MOVE/LIFT/DROP/CHARGE robot actions. The interface is kept for vehicle
// state queries which still have value for telemetry/UI.
public interface IVehicleCommandService
{
    Task<StandardRobotState?> GetVehicleStateAsync(Guid vehicleId, CancellationToken cancellationToken = default);
}
