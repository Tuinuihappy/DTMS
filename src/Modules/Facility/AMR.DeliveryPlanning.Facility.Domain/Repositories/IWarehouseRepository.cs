using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.Facility.Domain.Entities;

namespace AMR.DeliveryPlanning.Facility.Domain.Repositories;

/// <summary>
/// Persistence boundary for <see cref="Warehouse"/>. Concrete impl
/// lives in Facility.Infrastructure (added in Phase 2.2 with the EF
/// schema). For Phase 2.1 only the interface is defined — keeps the
/// Domain layer self-contained pending the schema migration.
///
/// Filter conventions:
///   - Default queries include only IsActive=true (use ExcludeInactive=false
///     to get historical data for audit / reports)
///   - ResolveByCodeAsync is the canonical name → Id lookup used by
///     order ingest (operator types "WH-BKK-01" in the picker)
/// </summary>
public interface IWarehouseRepository
{
    Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lookup by unique code (e.g. "WH-BKK-01"). Returns null if not found.</summary>
    Task<Warehouse?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Resolve a code string to a Guid id. Optimized for ingest validation.</summary>
    Task<Guid?> ResolveByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch resolution — used by order import where many station codes
    /// need to be resolved to ids in one shot. Returns map; missing codes
    /// are absent from the dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<string, Guid>> ResolveBatchAsync(
        IEnumerable<string> codes,
        CancellationToken cancellationToken = default);

    /// <summary>List warehouses, optionally filtered by serviceMode + activity.</summary>
    Task<IReadOnlyList<Warehouse>> ListAsync(
        TransportMode? serviceMode = null,
        bool excludeInactive = true,
        CancellationToken cancellationToken = default);

    Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken = default);
    void Update(Warehouse warehouse);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
