namespace AMR.DeliveryPlanning.Planning.Domain.Services;

/// <summary>
/// Cross-module abstraction to query Fleet vehicles.
/// Implemented in Planning.Infrastructure, backed by Fleet DbContext.
/// </summary>
public interface IFleetVehicleProvider
{
    Task<List<VehicleCandidate>> GetIdleVehiclesAsync(CancellationToken cancellationToken = default);
}
