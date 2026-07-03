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
    // ManualDispatchOptions so the watchdog has a single source of
    // truth even if the plan is amended later. AckDeadline was dropped
    // in the pool cleanup (2026-07-03) — pool claim = ack + assign in
    // one atomic step, so no separate ack window exists.
    public DateTime? PickupDeadline { get; private set; }
    public DateTime? DropDeadline { get; private set; }

    private ManualTripExtension() { }

    public static ManualTripExtension AssignToOperator(
        Guid tripId,
        Guid operatorId,
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

    // ReassignToOperator removed (2026-07-03) alongside the admin
    // reassign endpoint — per ADR-011 §"Consequences", forcing a claimed
    // pool trip onto another operator breaks the single-owner CAS
    // invariant. If a legitimate handoff use case surfaces, model it as
    // "release back to pool" + a normal claim.
}
