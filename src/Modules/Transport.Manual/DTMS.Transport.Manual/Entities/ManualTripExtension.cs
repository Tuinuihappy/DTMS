namespace DTMS.Transport.Manual.Domain.Entities;

// Phase 4.1 — Mirror of AmrTripExtension (Phase 3b) for the Manual mode.
// One-to-zero-or-one with Dispatch.Trip via TripId PK/FK. Holds the
// Manual-specific data Dispatch's Trip aggregate doesn't know about —
// operator binding, geofence approval reference, POD pointers.
//
// Lifecycle: row is lazy-created by ManualDispatchStrategy when the trip
// is first assigned to an operator; subsequent operator actions
// (acknowledge, pickup, drop, complete) mutate it through dedicated
// methods. Trip aggregate keeps the canonical FSM (Created → InProgress
// → Completed/Failed/Cancelled); this extension tracks the per-action
// timestamps that don't belong on the core entity.
public class ManualTripExtension
{
    // PK + FK to Dispatch.Trip.Id — enforced at the schema level via
    // unique index on TripId. No separate Id column needed.
    public Guid TripId { get; private set; }

    // The operator who owns this trip — set on assignment, never changes
    // (reassignment creates a new ManualTripExtension and archives the old
    // one to ManualTripReassignmentHistory, mirroring AMR pattern from
    // Phase 3d).
    public Guid OperatorId { get; private set; }
    public DateTime AssignedAt { get; private set; }

    // SLA timestamps — populated as operator progresses. Null means the
    // step hasn't happened yet. AcknowledgedAt > AssignedAt > PickedUpAt > DroppedAt.
    public DateTime? AcknowledgedAt { get; private set; }
    public DateTime? PickedUpAt { get; private set; }
    public DateTime? DroppedAt { get; private set; }

    // Geofence override reference — null = operator was inside the
    // geofence (normal case); set = override was approved (audit pointer
    // to the GeofenceOverrideRequest row that authorized the action).
    public Guid? PickupGeofenceOverrideId { get; private set; }
    public Guid? DropGeofenceOverrideId { get; private set; }

    // POD pointers — MinIO object keys (per ADR-015). The full
    // ProofOfDelivery entity (signature, metadata) still lives in
    // Dispatch — these are convenience pointers for the operator app to
    // read back without crossing module boundaries.
    public string? PickupPodKey { get; private set; }
    public string? DropPodKey { get; private set; }

    // SLA deadlines snapshot — set on assignment from
    // ManualDispatchPlan.SlaWindow so the watchdog has a single source
    // of truth even if the plan is amended later.
    public DateTime? AckDeadline { get; private set; }
    public DateTime? PickupDeadline { get; private set; }
    public DateTime? DropDeadline { get; private set; }

    private ManualTripExtension() { }

    public static ManualTripExtension AssignToOperator(
        Guid tripId,
        Guid operatorId,
        DateTime? ackDeadline,
        DateTime? pickupDeadline,
        DateTime? dropDeadline)
    {
        if (tripId == Guid.Empty)
            throw new ArgumentException("TripId must not be empty.", nameof(tripId));
        if (operatorId == Guid.Empty)
            throw new ArgumentException("OperatorId must not be empty.", nameof(operatorId));

        return new ManualTripExtension
        {
            TripId = tripId,
            OperatorId = operatorId,
            AssignedAt = DateTime.UtcNow,
            AckDeadline = ackDeadline,
            PickupDeadline = pickupDeadline,
            DropDeadline = dropDeadline,
        };
    }

    public void MarkAcknowledged()
    {
        if (AcknowledgedAt.HasValue) return;       // idempotent — operator may double-tap
        AcknowledgedAt = DateTime.UtcNow;
    }

    public void MarkPickedUp(string? podKey, Guid? overrideId)
    {
        if (!AcknowledgedAt.HasValue)
            throw new InvalidOperationException("Cannot pickup before acknowledging — operator app should enforce ordering.");
        if (PickedUpAt.HasValue) return;
        PickedUpAt = DateTime.UtcNow;
        PickupPodKey = podKey;
        PickupGeofenceOverrideId = overrideId;
    }

    public void MarkDropped(string? podKey, Guid? overrideId)
    {
        if (!PickedUpAt.HasValue)
            throw new InvalidOperationException("Cannot drop before pickup.");
        if (DroppedAt.HasValue) return;
        DroppedAt = DateTime.UtcNow;
        DropPodKey = podKey;
        DropGeofenceOverrideId = overrideId;
    }

    // Phase 4.6 — Dispatcher-initiated reassignment. Swaps the bound
    // operator, resets the ack window (new operator needs fresh chance
    // to ack), and bumps the pickup deadline by the original ack window
    // size so the SLA clock isn't artificially tight against the new
    // operator. Drop deadline keeps its absolute value — customer SLA
    // is independent of who's carrying the package.
    //
    // Refuses to reassign once the trip has been dropped — at that
    // point the operator's already handed off and only Complete is
    // meaningful.
    public void ReassignToOperator(Guid newOperatorId, DateTime? newAckDeadline, DateTime? newPickupDeadline)
    {
        if (newOperatorId == Guid.Empty)
            throw new ArgumentException("OperatorId must not be empty.", nameof(newOperatorId));
        if (DroppedAt.HasValue)
            throw new InvalidOperationException("Cannot reassign a trip that has already been dropped.");
        if (newOperatorId == OperatorId)
            return;  // idempotent — no-op if same operator

        OperatorId = newOperatorId;
        // Reset progress so the new operator starts at the same FSM
        // position as a fresh assignment. POD keys carry forward only
        // if pickup already happened — the photos are still valid;
        // the new operator just needs to do the drop.
        if (!PickedUpAt.HasValue)
        {
            AcknowledgedAt = null;
            PickupPodKey = null;
            PickupGeofenceOverrideId = null;
        }
        AckDeadline = newAckDeadline ?? AckDeadline;
        PickupDeadline = newPickupDeadline ?? PickupDeadline;
        // DropDeadline preserved — customer SLA is operator-agnostic.
    }
}
