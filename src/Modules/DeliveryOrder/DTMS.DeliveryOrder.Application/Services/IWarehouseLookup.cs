namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

/// <summary>
/// Outcome of a warehouse lookup. Mirrors <see cref="StationLookupResult"/>
/// but for the Warehouse aggregate (per ADR-002 — buildings/sites distinct
/// from AMR stations inside them).
///
/// <para>Phase 2.6: lookup interface + adapter defined; order validation
/// (MarkAsValidated path) doesn't wire it in yet — that's deferred to
/// Phase 2.3+ once AmrStation carries FacilityId. For now this is the
/// surface Phase 4 Manual mode + Phase 5 Fleet mode will consume directly
/// (operator assignment uses warehouse scope; fleet provider matching
/// uses warehouse service areas).</para>
/// </summary>
public sealed record WarehouseLookupResult(
    Guid Id,
    string Code,
    string Name,
    bool IsActive);

public interface IWarehouseLookup
{
    Task<bool> ExistsAsync(Guid warehouseId, CancellationToken ct = default);
    Task<Guid?> ResolveByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Batch-resolve a mix of warehouse codes in a single round trip. Returns
    /// a case-insensitive map keyed by the caller's original input (so
    /// callers can match results back to input slots without case worry).
    /// Codes that don't match any warehouse are omitted from the result —
    /// caller decides whether that's a hard error.
    /// </summary>
    Task<IReadOnlyDictionary<string, WarehouseLookupResult>> ResolveBatchAsync(
        IReadOnlyList<string> warehouseCodes,
        CancellationToken ct = default);
}
