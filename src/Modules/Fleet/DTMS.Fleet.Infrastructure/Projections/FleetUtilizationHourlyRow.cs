namespace DTMS.Fleet.Infrastructure.Projections;

/// <summary>
/// Phase P3.2 — Hour-bucketed snapshot of fleet utilization. Written by
/// <c>FleetUtilizationSnapshotService</c> once per hour (and on startup
/// for the current hour, so the dashboard has a row to show
/// immediately).
///
/// <para>Semantics: this is a <b>state snapshot</b>, not a transition
/// counter. <c>Total</c> = sum of the other columns excluding
/// LowBattery (which counts a sub-condition that overlaps with any
/// active state).</para>
/// </summary>
public class FleetUtilizationHourlyRow
{
    public Guid Id { get; private set; }
    public DateTime BucketHour { get; private set; }

    public int Active { get; private set; }
    public int Busy { get; private set; }
    public int Idle { get; private set; }
    public int Charging { get; private set; }
    public int Maintenance { get; private set; }
    public int LowBattery { get; private set; }
    public int Offline { get; private set; }
    public int Total { get; private set; }

    private FleetUtilizationHourlyRow() { }   // EF

    public FleetUtilizationHourlyRow(
        DateTime bucketHour,
        int active, int busy, int idle,
        int charging, int maintenance,
        int lowBattery, int offline,
        int total)
    {
        Id = Guid.NewGuid();
        BucketHour = bucketHour;
        Active = active;
        Busy = busy;
        Idle = idle;
        Charging = charging;
        Maintenance = maintenance;
        LowBattery = lowBattery;
        Offline = offline;
        Total = total;
    }
}
