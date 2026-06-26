namespace DTMS.Fleet.Application.Projections;

public record FleetUtilizationBucketEntry(
    DateTime BucketHour,
    int Active,
    int Busy,
    int Idle,
    int Charging,
    int Maintenance,
    int LowBattery,
    int Offline,
    int Total);

public interface IFleetUtilizationReadRepository
{
    Task<IReadOnlyList<FleetUtilizationBucketEntry>> GetRangeAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Most recent snapshot row — used by the KPI strip and snapshot
    /// service idle-tick to know whether a new bucket is needed.
    /// </summary>
    Task<FleetUtilizationBucketEntry?> GetLatestAsync(CancellationToken cancellationToken = default);
}
