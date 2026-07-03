using DTMS.Wms.Domain.Entities;

namespace DTMS.Wms.Domain.Repositories;

/// <summary>
/// Persistence contract for the WMS snapshot. Reads run on the hot path
/// (every order Submit resolves 2–20 codes) so batch/case-insensitive
/// resolvers are first-class here. Sync-time methods (GetAllCodes,
/// UpsertRange, MarkInactiveByCodes) live alongside for the background
/// service.
/// </summary>
public interface IWmsLocationRepository
{
    // ── Read (hot path) ─────────────────────────────────────────────
    Task<WmsLocation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WmsLocation?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Guid?> ResolveByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Batch-resolve a mix of location codes in a single round trip.
    /// Returns a case-insensitive dictionary keyed by the CALLER's original
    /// input casing so callers can match results back to their slots.
    /// Missing codes are omitted; caller decides whether that's a hard error.
    /// </summary>
    Task<IReadOnlyDictionary<string, WmsLocation>> ResolveBatchAsync(
        IReadOnlyList<string> codes, CancellationToken ct = default);

    Task<(IReadOnlyList<WmsLocation> Items, int Total)> QueryAsync(
        string? search,
        string? parentCode,
        int page,
        int pageSize,
        bool includeInactive,
        CancellationToken ct = default);

    // ── Sync (cold path) ────────────────────────────────────────────
    /// <summary>Load every currently-active LocationCode for sync diffing. Case-insensitive.</summary>
    Task<IReadOnlyDictionary<string, WmsLocation>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(WmsLocation location, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
