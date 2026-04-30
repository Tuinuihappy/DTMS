namespace AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;

public interface IVendorAdapterFactory
{
    IVehicleCommandService GetAdapterForVehicle(Guid vehicleId);
    VehicleAdapterResolution GetAdapterResolutionForVehicle(Guid vehicleId);
}

public sealed record VehicleAdapterResolution(
    IVehicleCommandService Adapter,
    string AdapterKey,
    string? VendorVehicleKey);
