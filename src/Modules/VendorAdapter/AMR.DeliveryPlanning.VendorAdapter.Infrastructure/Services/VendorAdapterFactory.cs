using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Services;

public class VendorAdapterFactory : IVendorAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;

    public VendorAdapterFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IVehicleCommandService GetAdapterForVehicle(Guid vehicleId)
    {
        // In a real scenario, this would query the Fleet module or a cache to get the VehicleTypeId.
        // For Phase 3, we assume we want to use the Riot3CommandService.
        // Once multiple adapters are registered, we can resolve IEnumerable<IVehicleCommandService>
        // and pick the right one.
        
        var services = _serviceProvider.GetServices<IVehicleCommandService>();
        
        // For demonstration, we'll try to find a Riot adapter first, else fallback
        var adapter = services.FirstOrDefault(s => s.GetType().Name.Contains("Riot3")) 
                      ?? services.FirstOrDefault();

        if (adapter == null)
        {
            throw new InvalidOperationException("No suitable VendorAdapter found.");
        }

        return adapter;
    }
}
