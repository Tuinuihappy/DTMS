using AMR.DeliveryPlanning.Planning.Domain.Entities;

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

    Task<IReadOnlyList<ActionTemplate>> ListAsync(
        bool includeInactive = false,
        string? actionType = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(ActionTemplate template, CancellationToken cancellationToken = default);

    void Update(ActionTemplate template);

    void Remove(ActionTemplate template);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
