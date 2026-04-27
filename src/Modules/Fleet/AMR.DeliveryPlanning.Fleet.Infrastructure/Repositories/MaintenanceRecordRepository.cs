using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Repositories;

public class MaintenanceRecordRepository : IMaintenanceRecordRepository
{
    private readonly FleetDbContext _db;
    public MaintenanceRecordRepository(FleetDbContext db) => _db = db;

    public Task<MaintenanceRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.MaintenanceRecords.FindAsync(new object[] { id }, ct).AsTask();

    public Task<List<MaintenanceRecord>> GetByVehicleAsync(Guid vehicleId, CancellationToken ct = default)
        => _db.MaintenanceRecords.Where(r => r.VehicleId == vehicleId).ToListAsync(ct);

    public async Task AddAsync(MaintenanceRecord record, CancellationToken ct = default)
        => await _db.MaintenanceRecords.AddAsync(record, ct);

    public Task UpdateAsync(MaintenanceRecord record, CancellationToken ct = default)
    {
        _db.MaintenanceRecords.Update(record);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
