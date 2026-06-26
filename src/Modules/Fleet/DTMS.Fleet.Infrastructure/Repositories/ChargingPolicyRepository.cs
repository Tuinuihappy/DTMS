using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Fleet.Infrastructure.Repositories;

public class ChargingPolicyRepository : IChargingPolicyRepository
{
    private readonly FleetDbContext _db;
    public ChargingPolicyRepository(FleetDbContext db) => _db = db;

    public Task<ChargingPolicy?> GetByVehicleTypeAsync(Guid vehicleTypeId, CancellationToken ct = default)
        => _db.ChargingPolicies.FirstOrDefaultAsync(p => p.VehicleTypeId == vehicleTypeId, ct);

    public Task<List<ChargingPolicy>> GetAllAsync(CancellationToken ct = default)
        => _db.ChargingPolicies.ToListAsync(ct);

    public async Task AddAsync(ChargingPolicy policy, CancellationToken ct = default)
        => await _db.ChargingPolicies.AddAsync(policy, ct);

    public Task UpdateAsync(ChargingPolicy policy, CancellationToken ct = default)
    {
        _db.ChargingPolicies.Update(policy);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
