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
        => GetAdapterResolutionForVehicle(vehicleId).Adapter;

    public VehicleAdapterResolution GetAdapterResolutionForVehicle(Guid vehicleId)
    {
        // Look up the adapter key persisted when the vehicle was registered
        var vehicleIdentity = _fleetDb.Vehicles
            .Where(v => v.Id == vehicleId)
            .Select(v => new { v.AdapterKey, v.VendorVehicleKey })
            .FirstOrDefault();

        if (vehicleIdentity != null)
        {
            var mapped = ResolveByKey(vehicleIdentity.AdapterKey);
            if (mapped != null)
            {
                _logger.LogDebug("Using {Adapter} adapter for vehicle {VehicleId}", vehicleIdentity.AdapterKey, vehicleId);
                return new VehicleAdapterResolution(mapped, vehicleIdentity.AdapterKey, vehicleIdentity.VendorVehicleKey);
            }
        }

        // Fall back to RIOT3 (default for SEER AMRs)
        var services = _serviceProvider.GetServices<IVehicleCommandService>().ToList();
        var adapter = services.FirstOrDefault(s => s.GetType().Name.Contains("Riot3"))
                      ?? services.FirstOrDefault()
                      ?? throw new InvalidOperationException($"No VendorAdapter found for vehicle {vehicleId}.");

        _logger.LogDebug("Using RIOT3 fallback adapter for vehicle {VehicleId}", vehicleId);
        return new VehicleAdapterResolution(adapter, "riot3", null);
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
