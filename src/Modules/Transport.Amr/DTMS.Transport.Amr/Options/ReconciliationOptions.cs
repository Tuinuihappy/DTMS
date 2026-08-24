namespace DTMS.Transport.Amr.Options;

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
    /// Classification threshold only — the poller reconciles EVERY
    /// non-terminal envelope trip regardless of age (a late vendor-side
    /// cancel past a skip window used to wedge trips forever when its
    /// webhook dropped). Trips whose CreatedAt is older than this many
    /// hours count into the trips_stuck gauge (P1 "needs a human look"
    /// alert) and drop out of the inflight gauge that gates the
    /// notify-silence alert.
    /// </summary>
    public int StaleThresholdHours { get; set; } = 24;

    /// <summary>
    /// Self-heal sweep window (hours). Each tick also re-checks terminal
    /// trips completed within this window that never captured a vehicle
    /// (webhook drove the terminal transition, so the in-flight terminal
    /// backfill never ran). Kept short — a real backfill happens on the tick
    /// right after completion; the window only covers brief outages. Trips
    /// drop out permanently once their snapshot is captured, so this bounds
    /// the sweep size, not correctness.
    /// </summary>
    public int SelfHealWindowHours { get; set; } = 2;
}
