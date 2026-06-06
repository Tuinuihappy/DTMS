using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;

namespace AMR.DeliveryPlanning.Planning.Domain.Repositories;

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
    /// Paged list with total count. Returns the page slice (sorted by Name)
    /// plus the unfiltered-by-paging total so the caller can compute
    /// page count for the RIOT3-style envelope.
    /// </summary>
    Task<(IReadOnlyList<ActionTemplate> Items, long Total)> ListPagedAsync(
        int page,
        int size,
        bool includeInactive = false,
        ActionType? actionType = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(ActionTemplate template, CancellationToken cancellationToken = default);

    void Update(ActionTemplate template);

    void Remove(ActionTemplate template);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
