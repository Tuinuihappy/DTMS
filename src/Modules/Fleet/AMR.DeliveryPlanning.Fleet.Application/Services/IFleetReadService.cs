namespace AMR.DeliveryPlanning.Fleet.Application.Services;

public sealed record FleetVehicleAvailability(
    Guid VehicleId,
    double BatteryLevel,
    Guid VehicleTypeId,
    Guid? CurrentNodeId,
    IReadOnlyCollection<string>? Capabilities);

public sealed record VehicleAdapterIdentity(
    string AdapterKey,
    string? VendorVehicleKey);

public interface IFleetReadService
{
    Task<IReadOnlyList<FleetVehicleAvailability>> GetIdleVehiclesAsync(
        CancellationToken cancellationToken = default);

    Task<VehicleAdapterIdentity?> GetVehicleAdapterIdentityAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default);

    Task<Guid?> ResolveVehicleIdAsync(
        string adapterKey,
        string vendorVehicleKey,
        CancellationToken cancellationToken = default);
}
