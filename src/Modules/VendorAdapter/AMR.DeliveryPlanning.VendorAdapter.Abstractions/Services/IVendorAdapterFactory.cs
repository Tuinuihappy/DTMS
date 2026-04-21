namespace AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;

public interface IVendorAdapterFactory
{
    IVehicleCommandService GetAdapterForVehicle(Guid vehicleId);
}
