namespace AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;

public interface IVendorAdapterFactory
{
    Task<IVehicleCommandService> GetAdapterForVehicleAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default);

    Task<VehicleAdapterResolution> GetAdapterResolutionForVehicleAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default);
}

public sealed record VehicleAdapterResolution(
    IVehicleCommandService Adapter,
    string AdapterKey,
    string? VendorVehicleKey);

public interface IVendorAdapterRegistry
{
    IVehicleCommandService? Resolve(string adapterKey);
    IReadOnlyCollection<string> RegisteredKeys { get; }
}
