using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Enums;

namespace DTMS.Fleet.Domain.Repositories;

public interface ICarrierRepository
{
    Task<Carrier?> GetByCodeAsync(string carrierCode, CancellationToken ct = default);
    Task<Carrier?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>True when the code is already taken. Pre-check only — it cannot
    /// close the window before the INSERT, so the unique index stays the real
    /// guarantee (surfaced as 409 by ExceptionHandlingMiddleware).</summary>
    Task<bool> CodeExistsAsync(string carrierCode, CancellationToken ct = default);

    /// <summary>Same caveat as <see cref="CodeExistsAsync"/>. <paramref name="excludeId"/>
    /// lets an edit ignore the row being edited.</summary>
    Task<bool> BarcodeExistsAsync(string barcode, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>
    /// Filtered page of carriers plus the total matching count.
    /// Ordered by <c>CarrierCode</c> — without a deterministic ORDER BY,
    /// PostgreSQL makes no ordering promise and rows silently duplicate or
    /// vanish between pages.
    /// </summary>
    Task<(IReadOnlyList<Carrier> Rows, int TotalCount)> SearchAsync(
        CarrierStatus? status,
        Guid? carrierTypeId,
        string? query,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task AddAsync(Carrier carrier, CancellationToken ct = default);
    void Remove(Carrier carrier);
    Task SaveChangesAsync(CancellationToken ct = default);
}
