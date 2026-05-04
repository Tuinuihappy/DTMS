using AMR.DeliveryPlanning.Fleet.Application.Services;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Services;

internal sealed class VehicleIdentityResolver : IVehicleIdentityResolver
{
    private readonly IFleetReadService _fleetReadService;

    public VehicleIdentityResolver(IFleetReadService fleetReadService)
    {
        _fleetReadService = fleetReadService;
    }

    public async Task<Guid?> ResolveVehicleIdAsync(string adapterKey, string vendorVehicleKey, CancellationToken cancellationToken = default)
        => await _fleetReadService.ResolveVehicleIdAsync(adapterKey, vendorVehicleKey, cancellationToken);
}
