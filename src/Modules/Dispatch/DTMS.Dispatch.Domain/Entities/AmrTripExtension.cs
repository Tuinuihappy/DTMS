using DTMS.Dispatch.Domain.Enums;

namespace DTMS.Dispatch.Domain.Entities;

// Phase 3b — AMR-specific data lifted off the Trip core. Manual / Fleet
// trips simply leave the navigation null; AMR trips persist one row per
// trip in dispatch."AmrTripExtensions" (1:0..1). Pattern mirrors the
// ManualTripExtension / FleetTripExtension entities planned for Phase 4-5.
//
// Phase 3d (vehicle reassignment fix) — the vehicle pointer fields
// (VendorVehicleKey, VendorVehicleName) are now a CACHE of the most
// recent assignment from VehicleAssignments. RecordVehicleAssignment
// is the single mutation entry point: append-to-history + update cache
// in one step. Earlier first-write-wins behaviour silently dropped
// real-vendor webhooks if any earlier (possibly fake) write had landed
// — surfaced by today's smoke test where a simulated webhook stuck a
// fake vehicleKey and PASS commands routed to the wrong robot.
//
// Lifecycle: created on demand the first time any AMR-specific field is
// set (CreateForEnvelope with a vendorOrderKey, or MarkVendorStarted
// with a vehicle key). Pause sets PauseSource; Resume clears it.
public class AmrTripExtension
{
    public Guid TripId { get; private set; }

    // RIOT3-assigned key returned in the envelope-dispatch response. May
    // be null when the vendor accepted but didn't echo a key — webhooks
    // still correlate via the UpperKey on the Trip core.
    public string? VendorOrderKey { get; private set; }

    // CACHE of the most-recent vehicle assignment. RecordVehicleAssignment
    // keeps this in sync with VehicleAssignments.LastOrDefault(). Kept
    // as a field (not a computed property) so EF queries that filter or
    // project by current vehicle key can run without a per-row JOIN to
    // the history table.
    public string? VendorVehicleKey { get; private set; }
    public string? VendorVehicleName { get; private set; }

    // Records WHICH vendor event paused this trip so Resume can pick
    // the matching command type. Null while not paused (cleared on
    // Resume / terminal transitions).
    public VendorPauseSource? VendorPauseSource { get; private set; }

    // Phase 3d — full audit of every vehicleKey reassignment. Append-only;
    // populated by RecordVehicleAssignment (idempotent — duplicate
    // webhooks don't grow the history). Operator dashboards can render
    // the timeline; PASS commands target VendorVehicleKey (cache) which
    // is always the latest entry's key.
    private readonly List<AmrVehicleAssignment> _vehicleAssignments = new();
    public IReadOnlyList<AmrVehicleAssignment> VehicleAssignments => _vehicleAssignments;

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
        // the webhook never overwrites a captured key. Unlike vehicleKey
        // (which can legitimately reassign), the orderKey is RIOT3's
        // identifier for the trip itself and never changes.
        VendorOrderKey ??= vendorOrderKey.Trim();
    }

    // Phase 3d — replaces the old first-write-wins AttachVehicle. Every
    // TASK_PROCESSING webhook calls this; the entity decides whether the
    // payload represents a real reassignment (append + update cache) or
    // a duplicate of the existing one (no-op).
    public void RecordVehicleAssignment(
        string? vendorVehicleKey, string? vendorVehicleName,
        string source, DateTime? assignedAt = null)
    {
        if (string.IsNullOrWhiteSpace(vendorVehicleKey)) return;
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source must not be empty.", nameof(source));

        // Idempotency: identical to the most recent assignment? Skip.
        // Both key AND name must match so a name-only update after a
        // key-only earlier webhook still records the addition. Empty /
        // null name compares case-sensitively to whatever was stored
        // before — operators see "(none)" turn into "FAN1_NO5" as a
        // distinct event.
        var last = _vehicleAssignments.LastOrDefault();
        if (last is not null
            && string.Equals(last.VendorVehicleKey, vendorVehicleKey, StringComparison.Ordinal)
            && string.Equals(last.VendorVehicleName, vendorVehicleName, StringComparison.Ordinal))
        {
            return;
        }

        var assignment = AmrVehicleAssignment.Create(
            tripId: TripId,
            sequence: _vehicleAssignments.Count + 1,
            vendorVehicleKey: vendorVehicleKey,
            vendorVehicleName: vendorVehicleName,
            assignedAt: assignedAt ?? DateTime.UtcNow,
            source: source);
        _vehicleAssignments.Add(assignment);

        // Update cache pointers — last-write-wins on these so any code
        // path (handlers, queries, projections) reading current vehicle
        // always sees the latest assignment without traversing history.
        VendorVehicleKey = vendorVehicleKey;
        VendorVehicleName = vendorVehicleName;
    }

    public void SetPauseSource(VendorPauseSource source) => VendorPauseSource = source;

    public void ClearPauseSource() => VendorPauseSource = null;
}
