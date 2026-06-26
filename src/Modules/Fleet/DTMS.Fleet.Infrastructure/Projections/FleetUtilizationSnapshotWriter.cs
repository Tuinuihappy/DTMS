using DTMS.Fleet.Application.Projections;
using DTMS.Fleet.Domain.Enums;
using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Fleet.Infrastructure.Projections;

/// <summary>
/// Phase P3.2 — Counts vehicles by state and UPSERTs a row into
/// <c>FleetUtilizationHourly</c> for the current hour bucket. Called
/// from the background service every minute (cheap: one COUNT GROUP BY
/// + one UPSERT). Bucket-grained idempotency: multiple calls within
/// the same hour just overwrite the row.
///
/// <para><b>State buckets</b> aggregate the VehicleState enum into the
/// dashboard's 5 columns:</para>
/// <list type="bullet">
///   <item>Busy = Moving + Working (robot is doing trip-work)</item>
///   <item>Idle = Idle (ready, not assigned)</item>
///   <item>Charging</item>
///   <item>Maintenance + Error (offline-for-cause)</item>
///   <item>Offline (genuinely unreachable)</item>
/// </list>
/// LowBattery is a sub-condition counted separately (battery &lt; 20%)
/// so it overlaps with any active state.
/// </summary>
public class FleetUtilizationSnapshotWriter : IFleetUtilizationSnapshotWriter
{
    private const double LowBatteryThreshold = 0.20;

    private readonly FleetDbContext _db;

    public FleetUtilizationSnapshotWriter(FleetDbContext db) => _db = db;

    public async Task UpsertCurrentBucketAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var bucket = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

        // Single SELECT — Vehicle is a small reference table so the
        // group-count fits comfortably in memory. If fleet grows past
        // ~10k robots, switch to a server-side GROUP BY projection.
        var vehicles = await _db.Vehicles
            .AsNoTracking()
            .Select(v => new { v.State, v.BatteryLevel })
            .ToListAsync(cancellationToken);

        var busy        = vehicles.Count(v => v.State is VehicleState.Moving or VehicleState.Working);
        var idle        = vehicles.Count(v => v.State == VehicleState.Idle);
        var charging    = vehicles.Count(v => v.State == VehicleState.Charging);
        var maintenance = vehicles.Count(v => v.State is VehicleState.Maintenance or VehicleState.Error);
        var offline     = vehicles.Count(v => v.State == VehicleState.Offline);
        var total       = vehicles.Count;
        var active      = total - offline - maintenance;
        var lowBattery  = vehicles.Count(v => v.BatteryLevel < LowBatteryThreshold
                                           && v.State != VehicleState.Offline);

        var existing = await _db.FleetUtilizationHourly
            .FirstOrDefaultAsync(r => r.BucketHour == bucket, cancellationToken);

        if (existing is not null)
        {
            // Overwrite by removing + re-adding — the row has no setters,
            // and EF tracking is cheap enough at the snapshot cadence.
            _db.FleetUtilizationHourly.Remove(existing);
        }

        _db.FleetUtilizationHourly.Add(new FleetUtilizationHourlyRow(
            bucket, active, busy, idle, charging,
            maintenance, lowBattery, offline, total));

        await _db.SaveChangesAsync(cancellationToken);
    }
}
