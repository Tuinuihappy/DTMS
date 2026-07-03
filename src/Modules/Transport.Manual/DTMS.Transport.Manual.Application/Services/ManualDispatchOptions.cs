namespace DTMS.Transport.Manual.Application.Services;

// WMS PR-4b — SLA windows stamped onto ManualTripExtension at pool claim
// time. AcknowledgeTripCommandHandler reads these to derive pickup/drop
// deadlines when an operator successfully claims a trip. The (yet-unbuilt)
// ManualTripSlaWatchdog will read them back to flag stalled trips for
// dispatcher attention.
//
// Defaults err on the relaxed side so test smokes don't trip the
// watchdog immediately; tighten in prod via appsettings.
public sealed class ManualDispatchOptions
{
    public const string SectionName = "TransportModes:Manual:Dispatch";

    // From claim time — operator must reach pickup within this window.
    public int PickupSlaMinutes { get; set; } = 30;

    // From PickedUpAt — operator must reach drop within this window.
    public int DropSlaMinutes { get; set; } = 120;

    // Kill switch — lets ops disable Manual dispatch entirely (e.g. during
    // an incident) without redeploying. When false the strategy short-
    // circuits at DispatchGroupAsync entry.
    public bool EnableDispatch { get; set; } = true;

    // ── Admin reassign flow (deprecated, defer for later cleanup) ────
    // The dispatcher's "reassign this trip to operator X" admin command
    // still reads these. That command is inconsistent with the pool
    // model — pool trips claim themselves; forced admin reassign is a
    // race-safety hazard. Slated for removal in a follow-up cleanup PR;
    // keeping the fields for now so the admin endpoint still compiles.
    public int AckSlaMinutes { get; set; } = 5;
    public string PushTitleTemplate { get; set; } = "New delivery: {0}";
    public string PushBodyTemplate { get; set; } = "Tap to view trip details.";
    public string PushTargetUrl { get; set; } = "/m/trips";
}
