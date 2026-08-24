using DTMS.Planning.Domain.Entities;

namespace DTMS.Planning.Domain.Repositories;

public interface IOrderTemplateRepository
{
    Task<OrderTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<OrderTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paged list with total count. Returns the page slice plus the
    /// unfiltered-by-paging total so the caller can compute page count
    /// for the RIOT3-style envelope. <paramref name="search"/> is a
    /// case-insensitive substring match against Name, Description, the
    /// appoint-vehicle/group names, and mission actionTemplateName
    /// references. <paramref name="isActive"/> filters to exactly that
    /// state when set and takes precedence over
    /// <paramref name="includeInactive"/>; when null the legacy
    /// includeInactive semantics apply (false → active only). Accepts an
    /// optional sort column + direction (default name asc when omitted).
    /// </summary>
    Task<(IReadOnlyList<OrderTemplate> Items, long Total)> ListPagedAsync(
        int page,
        int size,
        bool includeInactive = false,
        string? search = null,
        bool? isActive = null,
        string? sortBy = null,
        bool sortDescending = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unfiltered catalog counters for the KPI strip: total, active,
    /// average missions per template, and how many templates carry any
    /// vehicle/queue binding hint.
    /// </summary>
    Task<(int Total, int Active, double AvgMissions, int WithVehicleBinding)> GetStatsAsync(
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
