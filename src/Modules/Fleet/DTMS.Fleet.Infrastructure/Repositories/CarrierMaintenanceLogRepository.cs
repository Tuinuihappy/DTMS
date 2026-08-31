using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Fleet.Infrastructure.Repositories;

/// <summary>
/// No SaveChangesAsync of its own: every command that writes a log row also
/// changes the carrier's status, and the two must land together. Handlers save
/// through <see cref="ICarrierRepository"/> once — both repositories share the
/// same scoped <see cref="FleetDbContext"/>, so a single call persists both.
/// Saving twice would split one state change across two transactions.
/// </summary>
public class CarrierMaintenanceLogRepository : ICarrierMaintenanceLogRepository
{
    private readonly FleetDbContext _db;

    public CarrierMaintenanceLogRepository(FleetDbContext db) => _db = db;

    public Task<CarrierMaintenanceLog?> GetOpenForCarrierAsync(Guid carrierId, CancellationToken ct = default)
        => _db.CarrierMaintenanceLogs
            .FirstOrDefaultAsync(l => l.CarrierId == carrierId && l.EndedAt == null, ct);

    public Task<int> CountForCarrierAsync(Guid carrierId, CancellationToken ct = default)
        => _db.CarrierMaintenanceLogs.CountAsync(l => l.CarrierId == carrierId, ct);

    public Task<List<CarrierMaintenanceLog>> GetHistoryAsync(Guid carrierId, CancellationToken ct = default)
        => _db.CarrierMaintenanceLogs
            .AsNoTracking()
            .Where(l => l.CarrierId == carrierId)
            .OrderByDescending(l => l.StartedAt)
            .ToListAsync(ct);

    public async Task AddAsync(CarrierMaintenanceLog log, CancellationToken ct = default)
        => await _db.CarrierMaintenanceLogs.AddAsync(log, ct);
}
