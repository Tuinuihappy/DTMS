using AMR.DeliveryPlanning.Planning.Domain.Entities;

namespace AMR.DeliveryPlanning.Planning.Domain.Repositories;

public interface IOrderTemplateRepository
{
    Task<OrderTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<OrderTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paged list with total count. Returns the page slice (sorted by Name)
    /// plus the unfiltered-by-paging total so the caller can compute
    /// page count for the RIOT3-style envelope.
    /// </summary>
    Task<(IReadOnlyList<OrderTemplate> Items, long Total)> ListPagedAsync(
        int page,
        int size,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find an active template matching the given route (pickup → drop station).
    /// Used by the Planning consumer when a DeliveryOrder is confirmed to
    /// select which RIOT3 envelope to instantiate. Returns null when no
    /// route-specific template exists for this pair.
    /// </summary>
    Task<OrderTemplate?> FindByRouteAsync(
        Guid pickupStationId,
        Guid dropStationId,
        CancellationToken cancellationToken = default);

    Task AddAsync(OrderTemplate template, CancellationToken cancellationToken = default);

    void Update(OrderTemplate template);

    void Remove(OrderTemplate template);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
