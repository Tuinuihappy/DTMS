using AMR.DeliveryPlanning.Planning.Domain.Entities;

namespace AMR.DeliveryPlanning.Planning.Domain.Repositories;

public interface IOrderTemplateRepository
{
    Task<OrderTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<OrderTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderTemplate>> ListAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task AddAsync(OrderTemplate template, CancellationToken cancellationToken = default);

    void Update(OrderTemplate template);

    void Remove(OrderTemplate template);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
