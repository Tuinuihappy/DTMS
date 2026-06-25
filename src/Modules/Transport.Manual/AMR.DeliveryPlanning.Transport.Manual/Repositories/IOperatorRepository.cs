using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;

namespace AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;

// Phase 4.2 — Operator aggregate persistence. Most lookups hit the
// unique EmployeeCode index (the External Auth user identifier);
// internal trips/projections also fetch by GUID Id.
public interface IOperatorRepository
{
    Task<Operator?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Operator?> GetByEmployeeCodeAsync(string employeeCode, CancellationToken ct = default);

    // Includes Certifications + PushSubscriptions for endpoints that
    // render the operator's full profile (push fanout, assignment policy).
    Task<Operator?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<Operator?> GetByEmployeeCodeWithDetailsAsync(string employeeCode, CancellationToken ct = default);

    // Phase 4.4 — Eligible-for-assignment query for ManualDispatchStrategy.
    // Returns Active + CurrentTripId IS NULL operators, ordered so the
    // assignment policy can take the first match. preferredWarehouseId
    // floats operators whose PrimaryWarehouseId matches to the top;
    // unscoped operators come after.
    Task<IReadOnlyList<Operator>> GetEligibleForAssignmentAsync(
        Guid? preferredWarehouseId, CancellationToken ct = default);

    // Phase 4.6 — Dispatcher console "operator board" feed. Returns
    // ALL operators (any status, with or without an active trip) so
    // ops can see who's on shift, who's busy, who's deactivated.
    Task<IReadOnlyList<Operator>> ListAllAsync(CancellationToken ct = default);

    Task AddAsync(Operator op, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IGeofenceOverrideRequestRepository
{
    Task<GeofenceOverrideRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // "Is there an approved override I can use for this leg?" — the
    // pickup/drop endpoints call this after a geofence-fail to decide
    // whether to honour the operator's claim. Approved + not expired only.
    Task<GeofenceOverrideRequest?> GetApprovedForTripLegAsync(
        Guid tripId, Guid operatorId, Guid expectedWarehouseId, CancellationToken ct = default);

    // Phase 4.6 — Dispatcher feed: "what overrides are waiting for me?"
    // Pending only — Approved/Denied/Expired stay in the table for
    // audit but don't surface in the queue.
    Task<IReadOnlyList<GeofenceOverrideRequest>> ListPendingAsync(CancellationToken ct = default);

    Task AddAsync(GeofenceOverrideRequest request, CancellationToken ct = default);
    void Update(GeofenceOverrideRequest request);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IManualTripExtensionRepository
{
    Task<ManualTripExtension?> GetByTripIdAsync(Guid tripId, CancellationToken ct = default);
    Task<IReadOnlyList<ManualTripExtension>> GetByOperatorIdAsync(Guid operatorId, CancellationToken ct = default);

    // Phase 4.6 — Dispatcher feed: all active (not-yet-dropped) Manual
    // trips so the operator board can render a "who's carrying what"
    // table cross-cutting the operator list.
    Task<IReadOnlyList<ManualTripExtension>> ListActiveAsync(CancellationToken ct = default);

    Task AddAsync(ManualTripExtension extension, CancellationToken ct = default);
    void Update(ManualTripExtension extension);
    Task SaveChangesAsync(CancellationToken ct = default);
}
