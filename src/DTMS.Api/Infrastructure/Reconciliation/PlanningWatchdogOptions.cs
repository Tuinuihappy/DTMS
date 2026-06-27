namespace AMR.DeliveryPlanning.Api.Infrastructure.Reconciliation;

/// <summary>
/// Runtime knobs for <see cref="PlanningReconciliationService"/>. Bound to
/// the <c>PlanningWatchdog</c> configuration section so ops can toggle the
/// service or tune intervals without redeploying.
/// </summary>
public class PlanningWatchdogOptions
{
    public const string SectionName = "PlanningWatchdog";

    /// <summary>
    /// Master kill switch. When false the BackgroundService still runs but
    /// each tick exits early. Toggle off if the watchdog is misbehaving
    /// (e.g. re-firing in a tight loop because of a broken consumer).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often to scan for stuck orders. Default 60s.</summary>
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// An order is considered "stuck" if it has been at Status=Planned with
    /// no Trip for at least this long. Must be safely longer than the
    /// expected Planning-consumer completion time (typically 10-30s) to
    /// avoid replaying healthy in-flight work. Default 2 minutes.
    /// </summary>
    public int StaleThresholdSeconds { get; set; } = 120;

    /// <summary>
    /// Don't re-fire the same order more than once within this window even
    /// if the scan still finds it. Protects against replay storms when the
    /// consumer is wedged. Default 5 minutes.
    /// </summary>
    public int ReplayDedupSeconds { get; set; } = 300;

    /// <summary>
    /// Cap per tick so a giant backlog (e.g. RabbitMQ outage cleared) doesn't
    /// flood the bus with thousands of replays simultaneously. Default 50.
    /// </summary>
    public int MaxReplaysPerTick { get; set; } = 50;
}
