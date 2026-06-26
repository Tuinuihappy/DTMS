using DTMS.Fleet.Application.Projections;
using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Fleet.Infrastructure.Projections;

public class FleetUtilizationReadRepository : IFleetUtilizationReadRepository
{
    private readonly FleetDbContext _db;

    public FleetUtilizationReadRepository(FleetDbContext db) => _db = db;

    public async Task<IReadOnlyList<FleetUtilizationBucketEntry>> GetRangeAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _db.FleetUtilizationHourly
            .AsNoTracking()
            .Where(r => r.BucketHour >= fromUtc && r.BucketHour < toUtc)
            .OrderBy(r => r.BucketHour)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task<FleetUtilizationBucketEntry?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.FleetUtilizationHourly
            .AsNoTracking()
            .OrderByDescending(r => r.BucketHour)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : Map(row);
    }

    private static FleetUtilizationBucketEntry Map(FleetUtilizationHourlyRow r) =>
        new(r.BucketHour, r.Active, r.Busy, r.Idle, r.Charging,
            r.Maintenance, r.LowBattery, r.Offline, r.Total);
}
