using AMR.DeliveryPlanning.Fleet.Application.Services;
using AMR.DeliveryPlanning.Fleet.Domain.Enums;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Services;

public sealed class FleetReadService : IFleetReadService
{
    private readonly FleetDbContext _db;

    public FleetReadService(FleetDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<FleetVehicleAvailability>> GetIdleVehiclesAsync(
        CancellationToken cancellationToken = default)
    {
        var vehicles = await _db.Vehicles
            .AsNoTracking()
            .Where(v => v.State == VehicleState.Idle && v.BatteryLevel > 20)
            .ToListAsync(cancellationToken);

        var typeIds = vehicles.Select(v => v.VehicleTypeId).Distinct().ToList();
        var vehicleTypes = await _db.VehicleTypes
            .AsNoTracking()
            .Where(vt => typeIds.Contains(vt.Id))
            .ToDictionaryAsync(vt => vt.Id, cancellationToken);

        return vehicles
            .Select(v =>
            {
                vehicleTypes.TryGetValue(v.VehicleTypeId, out var vehicleType);
                return new FleetVehicleAvailability(
                    v.Id,
                    v.BatteryLevel,
                    v.VehicleTypeId,
                    v.CurrentNodeId,
                    vehicleType?.Capabilities);
            })
            .ToList();
    }

    public Task<VehicleAdapterIdentity?> GetVehicleAdapterIdentityAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default)
    {
        return _db.Vehicles
            .AsNoTracking()
            .Where(v => v.Id == vehicleId)
            .Select(v => new VehicleAdapterIdentity(v.AdapterKey, v.VendorVehicleKey))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> ResolveVehicleIdAsync(
        string adapterKey,
        string vendorVehicleKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(adapterKey) || string.IsNullOrWhiteSpace(vendorVehicleKey))
            return null;

        var normalizedAdapterKey = adapterKey.Trim().ToLowerInvariant();
        var normalizedVendorKey = vendorVehicleKey.Trim();

        var mappedVehicleId = await _db.Vehicles
            .IgnoreQueryFilters()
            .Where(v => v.AdapterKey == normalizedAdapterKey && v.VendorVehicleKey == normalizedVendorKey)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (mappedVehicleId.HasValue)
            return mappedVehicleId;

        return Guid.TryParse(normalizedVendorKey, out var vehicleId)
            ? vehicleId
            : null;
    }
}
