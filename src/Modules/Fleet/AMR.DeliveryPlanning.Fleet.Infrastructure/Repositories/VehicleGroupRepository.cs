using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Repositories;

public class VehicleGroupRepository : IVehicleGroupRepository
{
    private readonly FleetDbContext _db;
    public VehicleGroupRepository(FleetDbContext db) => _db = db;

    public Task<VehicleGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.VehicleGroups.Include(g => g.VehicleIds).FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<List<VehicleGroup>> GetAllAsync(CancellationToken ct = default)
        => _db.VehicleGroups.ToListAsync(ct);

    public async Task AddAsync(VehicleGroup group, CancellationToken ct = default)
        => await _db.VehicleGroups.AddAsync(group, ct);

    public Task UpdateAsync(VehicleGroup group, CancellationToken ct = default)
    {
        _db.VehicleGroups.Update(group);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
