namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

// Phase 3d (Bug #2 — first-write-wins on VehicleKey) — captures every
// vehicleKey RIOT3 reports across a Trip's lifetime, in order. Replaces
// the old first-write-wins single-field behaviour that quietly dropped
// real-vendor webhooks if a fake / earlier webhook had already written
// any value (today's smoke surfaced this).
//
// Audit + correctness:
//   - History (this entity, many rows per trip) tells "when did robot X
//     pick up, when did it hand off to robot Y" — invaluable for ops
//     debugging "why did Trip Z take 4 hours".
//   - Cache pointers on AmrTripExtension (VendorVehicleKey, VendorVehicleName)
//     remain so EF queries that filter / project by current vehicle key
//     don't need a per-row JOIN to the history table.
//
// Idempotency:
//   - The webhook handler's RecordVehicleAssignment call is idempotent
//     at the entity level: if the incoming (key, name) matches the LAST
//     assignment, no new row is appended. Duplicate TASK_PROCESSING
//     webhooks (RIOT3 retries) therefore don't pollute history.
public class AmrVehicleAssignment
{
    public Guid Id { get; private set; }
    public Guid TripId { get; private set; }

    // 1-based monotonically-increasing within a Trip. The first
    // assignment (from the first TASK_PROCESSING webhook) is Sequence=1.
    // A reassignment lands as Sequence=2, etc.
    public int Sequence { get; private set; }

    // The RIOT3 deviceKey (e.g. "Delta6FAN1" or a GUID string). This is
    // what PASS commands target — the "current" assignment determines
    // which robot any operator-initiated command routes to.
    public string VendorVehicleKey { get; private set; } = string.Empty;

    // Human-readable label from RIOT3 processingVehicle.name. Display
    // only — UI surfaces it next to the key for operator clarity.
    public string? VendorVehicleName { get; private set; }

    public DateTime AssignedAt { get; private set; }

    // Where this assignment came from. Today: "TASK_PROCESSING" (vendor
    // webhook) or "backfill" (migration). Phase 4+ may add
    // "operator-reassign" if dispatcher console exposes a manual override.
    public string Source { get; private set; } = string.Empty;

    private AmrVehicleAssignment() { }

    internal static AmrVehicleAssignment Create(
        Guid tripId, int sequence, string vendorVehicleKey,
        string? vendorVehicleName, DateTime assignedAt, string source)
    {
        if (tripId == Guid.Empty)
            throw new ArgumentException("TripId must not be empty.", nameof(tripId));
        if (sequence < 1)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be >= 1.");
        if (string.IsNullOrWhiteSpace(vendorVehicleKey))
            throw new ArgumentException("VendorVehicleKey must not be empty.", nameof(vendorVehicleKey));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source must not be empty.", nameof(source));

        return new AmrVehicleAssignment
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            Sequence = sequence,
            VendorVehicleKey = vendorVehicleKey,
            VendorVehicleName = vendorVehicleName,
            AssignedAt = assignedAt,
            Source = source,
        };
    }
}
