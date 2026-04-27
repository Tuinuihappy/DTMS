using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;

public interface IDeliveryOrderRepository
{
    Task<Entities.DeliveryOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Entities.DeliveryOrder?> GetByOrderKeyAsync(string orderKey, CancellationToken cancellationToken = default);
    Task<List<Entities.DeliveryOrder>> GetByStatusAsync(OrderStatus status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task AddAsync(Entities.DeliveryOrder order, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<Entities.DeliveryOrder> orders, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.DeliveryOrder order, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IOrderAmendmentRepository
{
    Task AddAsync(OrderAmendment amendment, CancellationToken cancellationToken = default);
    Task<List<OrderAmendment>> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IOrderAuditEventRepository
{
    Task AddAsync(OrderAuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task<List<OrderAuditEvent>> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
