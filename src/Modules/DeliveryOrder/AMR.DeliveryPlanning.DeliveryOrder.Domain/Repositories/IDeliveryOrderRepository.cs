using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;

public interface IDeliveryOrderRepository
{
    Task<Entities.DeliveryOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Entities.DeliveryOrder?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Entities.DeliveryOrder>> GetByStatusAsync(OrderStatus status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<List<Entities.DeliveryOrder>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> CountAsync(OrderStatus? status, CancellationToken cancellationToken = default);
    Task<List<Entities.DeliveryOrder>> GetOrdersByItemSkusAsync(IEnumerable<string> skus, CancellationToken cancellationToken = default);
    Task<List<Entities.DeliveryOrder>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<(List<Item> Items, int TotalCount)> SearchItemsAsync(string? sku, CargoType? cargoType, ItemStatus? status, string? pickupLocationCode, string? dropLocationCode, string? partNo, string? wo, string? line, string? vendor, string? dateCode, string? tradingCode, string? inventoryNo, string? po, string? traceId, string? lotNo, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Item?> GetItemByIdAsync(Guid itemId, CancellationToken cancellationToken = default);
    Task AddAsync(Entities.DeliveryOrder order, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<Entities.DeliveryOrder> orders, CancellationToken cancellationToken = default);
    Task RemoveItemsAsync(IEnumerable<Entities.Item> items, CancellationToken cancellationToken = default);
    Task AddItemsAsync(IEnumerable<Entities.Item> items, CancellationToken cancellationToken = default);
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
