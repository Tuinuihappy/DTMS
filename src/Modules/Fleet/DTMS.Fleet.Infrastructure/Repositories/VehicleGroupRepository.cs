using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Repositories;

public class VehicleGroupRepository : IVehicleGroupRepository
{
    private readonly FleetDbContext _db;
    public VehicleGroupRepository(FleetDbContext db) => _db = db;

    public async Task<VehicleGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var group = await _db.VehicleGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group == null) return null;

        var vehicleIds = await _db.VehicleGroupMembers
            .Where(m => m.VehicleGroupId == id)
            .Select(m => m.VehicleId)
            .ToListAsync(ct);

        group.LoadVehicleIds(vehicleIds);
        return group;
    }

    public async Task<List<VehicleGroup>> GetAllAsync(CancellationToken ct = default)
    {
        var groups = await _db.VehicleGroups.ToListAsync(ct);
        if (groups.Count == 0) return groups;

        // Single query for all members — avoids N+1
        var groupIds = groups.Select(g => g.Id).ToList();
        var allMembers = await _db.VehicleGroupMembers
            .Where(m => groupIds.Contains(m.VehicleGroupId))
            .ToListAsync(ct);

        var byGroup = allMembers
            .GroupBy(m => m.VehicleGroupId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.VehicleId));

        foreach (var group in groups)
        {
            if (byGroup.TryGetValue(group.Id, out var ids))
                group.LoadVehicleIds(ids);
        }

        return groups;
    }

    public async Task AddAsync(VehicleGroup group, CancellationToken ct = default)
    {
        await _db.VehicleGroups.AddAsync(group, ct);
        AddMembers(group);
    }

    public async Task UpdateAsync(VehicleGroup group, CancellationToken ct = default)
    {
        _db.VehicleGroups.Update(group);
        await SyncMembersAsync(group, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    // On insert: simply add all current members
    private void AddMembers(VehicleGroup group)
    {
        _db.VehicleGroupMembers.AddRange(
            group.VehicleIds.Select(vid => new VehicleGroupMember
            {
                VehicleGroupId = group.Id,
                VehicleId = vid
            }));
    }

    // On update: delete-then-reinsert is safe for small collections and keeps
    // the logic simple. For groups with hundreds of vehicles, switch to a
    // set-difference approach (only delete removed, only insert added).
    private async Task SyncMembersAsync(VehicleGroup group, CancellationToken ct)
    {
        var existing = await _db.VehicleGroupMembers
            .Where(m => m.VehicleGroupId == group.Id)
            .ToListAsync(ct);

        _db.VehicleGroupMembers.RemoveRange(existing);
        _db.VehicleGroupMembers.AddRange(
            group.VehicleIds.Select(vid => new VehicleGroupMember
            {
                VehicleGroupId = group.Id,
                VehicleId = vid
            }));
    }
}
