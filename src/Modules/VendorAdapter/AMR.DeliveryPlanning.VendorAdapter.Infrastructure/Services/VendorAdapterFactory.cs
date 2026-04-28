using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Services;

public class VendorAdapterFactory : IVendorAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FleetDbContext _fleetDb;
    private readonly ILogger<VendorAdapterFactory> _logger;

    public VendorAdapterFactory(
        IServiceProvider serviceProvider,
        FleetDbContext fleetDb,
        ILogger<VendorAdapterFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _fleetDb = fleetDb;
        _logger = logger;
    }

    public IVehicleCommandService GetAdapterForVehicle(Guid vehicleId)
    {
        // Look up the adapter key persisted when the vehicle was registered
        var adapterKey = _fleetDb.Vehicles
            .Where(v => v.Id == vehicleId)
            .Select(v => v.AdapterKey)
            .FirstOrDefault();

        if (adapterKey != null)
        {
            var mapped = ResolveByKey(adapterKey);
            if (mapped != null)
            {
                _logger.LogDebug("Using {Adapter} adapter for vehicle {VehicleId}", adapterKey, vehicleId);
                return mapped;
            }
        }

        // Fall back to RIOT3 (default for SEER AMRs)
        var services = _serviceProvider.GetServices<IVehicleCommandService>().ToList();
        var adapter = services.FirstOrDefault(s => s.GetType().Name.Contains("Riot3"))
                      ?? services.FirstOrDefault()
                      ?? throw new InvalidOperationException($"No VendorAdapter found for vehicle {vehicleId}.");

        _logger.LogDebug("Using RIOT3 fallback adapter for vehicle {VehicleId}", vehicleId);
        return adapter;
    }

    private IVehicleCommandService? ResolveByKey(string key) => key switch
    {
        // "feeder" vehicles use the RIOT3 adapter — same vendor, same API protocol
        "feeder" or "riot3" => _serviceProvider.GetServices<IVehicleCommandService>()
                                   .FirstOrDefault(s => s.GetType().Name.Contains("Riot3")),
        "sim"               => _serviceProvider.GetServices<IVehicleCommandService>()
                                   .FirstOrDefault(s => s.GetType().Name.Contains("Simulator")),
        _                   => null
    };
}
