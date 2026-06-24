using AMR.DeliveryPlanning.Dispatch.Domain.Enums;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

// Phase 3b — AMR-specific data lifted off the Trip core. Manual / Fleet
// trips simply leave the navigation null; AMR trips persist one row per
// trip in dispatch."AmrTripExtensions" (1:0..1). Pattern mirrors the
// ManualTripExtension / FleetTripExtension entities planned for Phase 4-5.
//
// Lifecycle: created on demand the first time any AMR-specific field is
// set (CreateForEnvelope with a vendorOrderKey, or MarkVendorStarted
// with a vehicle key). Pause sets PauseSource; Resume clears it.
//
// Why a separate entity vs Trip-owned property: Manual + Fleet will get
// their own extension entities. Modelling them as separate types lets
// the Dispatch.Domain layer stay mode-agnostic — Trip core compiles
// without knowing what RIOT3 is.
public class AmrTripExtension
{
    public Guid TripId { get; private set; }

    // RIOT3-assigned key returned in the envelope-dispatch response. May
    // be null when the vendor accepted but didn't echo a key — webhooks
    // still correlate via the UpperKey on the Trip core.
    public string? VendorOrderKey { get; private set; }

    // The robot's deviceKey reported on processingVehicle (e.g.
    // "Delta6FAN1") — first-write-wins, audit-only, never used for
    // correlation. VehicleId on Trip is the DTMS-side Guid; the two
    // are intentionally separate (Fleet lookup deferred).
    public string? VendorVehicleKey { get; private set; }

    // Human-readable robot label (e.g. "FAN1_STANDARD_NO5"). Display
    // only, also first-write-wins.
    public string? VendorVehicleName { get; private set; }

    // Records WHICH vendor event paused this trip so Resume can pick
    // the matching command type. Null while not paused (cleared on
    // Resume / terminal transitions).
    public VendorPauseSource? VendorPauseSource { get; private set; }

    private AmrTripExtension() { }

    public static AmrTripExtension Create(Guid tripId)
    {
        if (tripId == Guid.Empty)
            throw new ArgumentException("TripId must not be empty.", nameof(tripId));
        return new AmrTripExtension { TripId = tripId };
    }

    public void AttachVendorOrder(string vendorOrderKey)
    {
        if (string.IsNullOrWhiteSpace(vendorOrderKey)) return;
        // First write wins — the dispatcher writes once at create time;
        // the webhook never overwrites a captured key.
        VendorOrderKey ??= vendorOrderKey.Trim();
    }

    public void AttachVehicle(string? vendorVehicleKey, string? vendorVehicleName)
    {
        if (!string.IsNullOrWhiteSpace(vendorVehicleKey) && VendorVehicleKey is null)
            VendorVehicleKey = vendorVehicleKey;
        if (!string.IsNullOrWhiteSpace(vendorVehicleName) && VendorVehicleName is null)
            VendorVehicleName = vendorVehicleName;
    }

    public void SetPauseSource(VendorPauseSource source) => VendorPauseSource = source;

    public void ClearPauseSource() => VendorPauseSource = null;
}
