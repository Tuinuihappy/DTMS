using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Enums;

namespace DTMS.Planning.Domain.Repositories;

public interface IActionTemplateRepository
{
    Task<ActionTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Case-insensitive name lookup. Returns null when no template matches.
    /// Used both for resolving references from OrderTemplates and for
    /// enforcing uniqueness on create/rename.
    /// </summary>
    Task<ActionTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paged list with total count. Returns the page slice plus the
    /// unfiltered-by-paging total so the caller can compute page count for
    /// the RIOT3-style envelope. <paramref name="search"/> is a
    /// case-insensitive substring match against Name; <paramref name="sortBy"/>
    /// accepts "actionName" (default), "actionCategory", "modifiedAt",
    /// "isActive" — anything else falls back to Name asc.
    /// </summary>
    Task<(IReadOnlyList<ActionTemplate> Items, long Total)> ListPagedAsync(
        int page,
        int size,
        bool includeInactive = false,
        ActionCategory? actionCategory = null,
        string? search = null,
        string? sortBy = null,
        bool sortDescending = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unfiltered catalog counts for the KPI strip. Total counts every row;
    /// Active counts IsActive=true; Std/Act split by ActionCategory.
    /// </summary>
    Task<(int Total, int Active, int Std, int Act)> GetStatsAsync(
        CancellationToken cancellationToken = default);

    Task AddAsync(ActionTemplate template, CancellationToken cancellationToken = default);

    void Update(ActionTemplate template);

    void Remove(ActionTemplate template);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
