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

    Task AddAsync(GeofenceOverrideRequest request, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IManualTripExtensionRepository
{
    Task<ManualTripExtension?> GetByTripIdAsync(Guid tripId, CancellationToken ct = default);
    Task<IReadOnlyList<ManualTripExtension>> GetByOperatorIdAsync(Guid operatorId, CancellationToken ct = default);
    Task AddAsync(ManualTripExtension extension, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
