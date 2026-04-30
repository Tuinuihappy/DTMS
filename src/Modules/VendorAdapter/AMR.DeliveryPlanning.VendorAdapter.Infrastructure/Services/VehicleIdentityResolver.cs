using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Services;

internal sealed class VehicleIdentityResolver : IVehicleIdentityResolver
{
    private readonly FleetDbContext _fleetDb;

    public VehicleIdentityResolver(FleetDbContext fleetDb)
    {
        _fleetDb = fleetDb;
    }

    public async Task<Guid?> ResolveVehicleIdAsync(string adapterKey, string vendorVehicleKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(adapterKey) || string.IsNullOrWhiteSpace(vendorVehicleKey))
            return null;

        var normalizedAdapterKey = adapterKey.Trim().ToLowerInvariant();
        var normalizedVendorKey = vendorVehicleKey.Trim();

        var mappedVehicleId = await _fleetDb.Vehicles
            .IgnoreQueryFilters()
            .Where(v => v.AdapterKey == normalizedAdapterKey && v.VendorVehicleKey == normalizedVendorKey)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (mappedVehicleId.HasValue)
            return mappedVehicleId;

        // Backward-compatible path for existing test/dev payloads where deviceKey is the app VehicleId.
        if (!Guid.TryParse(normalizedVendorKey, out var vehicleId))
            return null;

        return vehicleId;
    }
}
