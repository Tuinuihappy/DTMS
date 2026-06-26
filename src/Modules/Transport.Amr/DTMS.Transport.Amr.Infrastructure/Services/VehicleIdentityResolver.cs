using DTMS.Fleet.Application.Services;
using DTMS.Transport.Abstractions.Services;

namespace DTMS.Transport.Amr.Infrastructure.Services;

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
