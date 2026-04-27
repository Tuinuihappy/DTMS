using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Services;

public class VendorAdapterFactory : IVendorAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VendorAdapterFactory> _logger;

    // vehicleId → adapterKey mapping (populated at runtime when vehicles register)
    private static readonly Dictionary<Guid, string> _vehicleAdapterMap = new();

    public VendorAdapterFactory(IServiceProvider serviceProvider, ILogger<VendorAdapterFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public static void RegisterVehicleAdapter(Guid vehicleId, string adapterKey)
        => _vehicleAdapterMap[vehicleId] = adapterKey;

    public IVehicleCommandService GetAdapterForVehicle(Guid vehicleId)
    {
        // Check runtime mapping first
        if (_vehicleAdapterMap.TryGetValue(vehicleId, out var key))
        {
            var mapped = ResolveByKey(key);
            if (mapped != null) return mapped;
        }

        // Fall back to RIOT3 adapter (default for SEER AMRs)
        var services = _serviceProvider.GetServices<IVehicleCommandService>().ToList();
        var adapter = services.FirstOrDefault(s => s.GetType().Name.Contains("Riot3"))
                      ?? services.FirstOrDefault();

        if (adapter == null)
            throw new InvalidOperationException($"No VendorAdapter found for vehicle {vehicleId}.");

        _logger.LogDebug("Using {Adapter} for vehicle {VehicleId}", adapter.GetType().Name, vehicleId);
        return adapter;
    }

    private IVehicleCommandService? ResolveByKey(string key) => key switch
    {
        "feeder" => _serviceProvider.GetServices<IVehicleCommandService>()
                        .FirstOrDefault(s => s.GetType().Name.Contains("Feeder")),
        "riot3"  => _serviceProvider.GetServices<IVehicleCommandService>()
                        .FirstOrDefault(s => s.GetType().Name.Contains("Riot3")),
        "sim"    => _serviceProvider.GetServices<IVehicleCommandService>()
                        .FirstOrDefault(s => s.GetType().Name.Contains("Simulator")),
        _        => null
    };
}
