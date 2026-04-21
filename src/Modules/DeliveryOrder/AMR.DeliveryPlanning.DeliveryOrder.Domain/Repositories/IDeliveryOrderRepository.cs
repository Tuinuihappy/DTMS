using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;

public interface IDeliveryOrderRepository
{
    Task<Entities.DeliveryOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Entities.DeliveryOrder order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.DeliveryOrder order, CancellationToken cancellationToken = default);
}
