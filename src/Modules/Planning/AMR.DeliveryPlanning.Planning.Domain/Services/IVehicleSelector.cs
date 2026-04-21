namespace AMR.DeliveryPlanning.Planning.Domain.Services;

public record VehicleCandidate(Guid VehicleId, double DistanceToPickup, double BatteryLevel);

public interface IVehicleSelector
{
    Task<VehicleCandidate?> SelectBestVehicleAsync(Guid pickupStationId, CancellationToken cancellationToken = default);
}
