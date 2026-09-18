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

    /// <summary>How many carriers hold this type. Feeds the carrier-type delete
    /// guard, which has to refuse before the FK does — Carriers.CarrierTypeId is
    /// ON DELETE RESTRICT, and a 23503 reaching the caller says nothing useful.</summary>
    Task<int> CountForCarrierTypeAsync(Guid carrierTypeId, CancellationToken ct = default);

    /// <summary>
    /// The same count for every type in <paramref name="carrierTypeIds"/>, in one
    /// query — what the catalogue list needs so no row counts its own carriers.
    /// Types with none are absent from the result.
    ///
    /// <para>A second method rather than a loop over the singular one, the same
    /// split <see cref="IAttachmentRepository.CountForOwnerAsync"/> and
    /// <see cref="IAttachmentRepository.GetSummariesForOwnersAsync"/> already
    /// make: one row asking about itself and a page asking about all its rows
    /// are different queries, and expressing the page as N of the first is how
    /// list endpoints quietly become N+1.</para>
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountsByCarrierTypeAsync(
        IReadOnlyCollection<Guid> carrierTypeIds, CancellationToken ct = default);

    Task AddAsync(Carrier carrier, CancellationToken ct = default);
    void Remove(Carrier carrier);
    Task SaveChangesAsync(CancellationToken ct = default);
}
