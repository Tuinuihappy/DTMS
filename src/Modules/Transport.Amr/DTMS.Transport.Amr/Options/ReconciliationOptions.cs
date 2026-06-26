namespace AMR.DeliveryPlanning.Transport.Amr.Options;

/// <summary>
/// Configuration for the RIOT3 reconciliation poller. The poller is the
/// safety net for envelope-dispatched trips: if a webhook gets dropped,
/// the next tick re-fetches state from RIOT3 and reconciles the Trip.
/// </summary>
public class ReconciliationOptions
{
    public const string SectionName = "Dispatch:Reconciliation";

    /// <summary>
    /// Off by default — flip on once envelope dispatch is active in the
    /// environment. When false, the BackgroundService is registered but
    /// does nothing per tick.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>How often the poller wakes (seconds).</summary>
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Trips whose CreatedAt is older than this many hours are skipped
    /// (no longer chased by the poller). Ops handles those manually.
    /// </summary>
    public int StaleThresholdHours { get; set; } = 24;
}
