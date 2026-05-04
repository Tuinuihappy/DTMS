using AMR.DeliveryPlanning.Fleet.Application.Services;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Services;

public class VendorAdapterFactory : IVendorAdapterFactory
{
    private readonly IFleetReadService _fleetReadService;
    private readonly IVendorAdapterRegistry _registry;
    private readonly ILogger<VendorAdapterFactory> _logger;

    public VendorAdapterFactory(
        IFleetReadService fleetReadService,
        IVendorAdapterRegistry registry,
        ILogger<VendorAdapterFactory> logger)
    {
        _fleetReadService = fleetReadService;
        _registry = registry;
        _logger = logger;
    }

    public async Task<IVehicleCommandService> GetAdapterForVehicleAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default)
    {
        var resolution = await GetAdapterResolutionForVehicleAsync(vehicleId, cancellationToken);
        return resolution.Adapter;
    }

    public async Task<VehicleAdapterResolution> GetAdapterResolutionForVehicleAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default)
    {
        var vehicleIdentity = await _fleetReadService.GetVehicleAdapterIdentityAsync(vehicleId, cancellationToken);
        if (vehicleIdentity is null)
            throw new InvalidOperationException($"Vehicle {vehicleId} has no registered adapter identity.");

        var adapterKey = vehicleIdentity.AdapterKey.Trim().ToLowerInvariant();
        var adapter = _registry.Resolve(adapterKey);
        if (adapter is null)
        {
            throw new InvalidOperationException(
                $"Vehicle {vehicleId} uses unregistered adapter key '{vehicleIdentity.AdapterKey}'. " +
                $"Registered adapter keys: {string.Join(", ", _registry.RegisteredKeys.OrderBy(k => k))}.");
        }

        _logger.LogDebug("Using {AdapterKey} adapter for vehicle {VehicleId}", adapterKey, vehicleId);
        return new VehicleAdapterResolution(adapter, adapterKey, vehicleIdentity.VendorVehicleKey);
    }
}
