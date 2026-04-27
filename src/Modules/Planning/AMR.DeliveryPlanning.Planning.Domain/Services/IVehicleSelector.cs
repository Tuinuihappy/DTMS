namespace AMR.DeliveryPlanning.Planning.Domain.Services;

public record VehicleCandidate(Guid VehicleId, double DistanceToPickup, double BatteryLevel,
    Guid VehicleTypeId = default, IReadOnlyCollection<string>? Capabilities = null);

public interface IVehicleSelector
{
    Task<VehicleCandidate?> SelectBestVehicleAsync(
        Guid pickupStationId,
        string? requiredCapability = null,
        CancellationToken cancellationToken = default);
}
