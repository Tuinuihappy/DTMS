namespace DTMS.Transport.Manual.Application.Services;

// Phase 4.4 — SLA windows the strategy stamps onto ManualTripExtension
// at assignment time. The (yet-unbuilt) ManualTripSlaWatchdog reads
// these deadlines to flag stalled trips for dispatcher attention.
//
// Defaults err on the relaxed side so test smokes don't trip the
// watchdog immediately; tighten in prod via appsettings.
public sealed class ManualDispatchOptions
{
    public const string SectionName = "TransportModes:Manual:Dispatch";

    // From AssignedAt — operator must acknowledge within this window.
    public int AckSlaMinutes { get; set; } = 5;

    // From AcknowledgedAt — operator must reach pickup warehouse.
    public int PickupSlaMinutes { get; set; } = 30;

    // From PickedUpAt — operator must reach drop warehouse.
    public int DropSlaMinutes { get; set; } = 120;

    // Notification payload knobs — Title / Body templates are baked
    // here so dispatcher ops can tweak wording without a code change.
    // {0} = order id (short form), {1} = warehouse code (when available).
    public string PushTitleTemplate { get; set; } = "New delivery: {0}";
    public string PushBodyTemplate { get; set; } = "Tap to view trip details.";
    public string PushTargetUrl { get; set; } = "/m/trips";

    // Lets ops cap dispatch attempts if the operator pool is exhausted.
    // The strategy doesn't retry within a single call — this is a
    // "should we even try" flag the consumer can use to drop the
    // dispatch entirely once Phase 4.6 retry logic lands.
    public bool EnableDispatch { get; set; } = true;
}
