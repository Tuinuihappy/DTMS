using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using ItemStatus = DTMS.DeliveryOrder.Domain.Enums.ItemStatus;

namespace DTMS.DeliveryOrder.Domain.Repositories;

public record DeliveryOrderSearchFilters(
    OrderStatus? Status,
    StatusBucket? Bucket,
    Priority? Priority,
    TransportMode? TransportMode,
    string? Search,
    string? SortBy,
    bool SortDescending);

public record DeliveryOrderStats(
    int Total,
    Dictionary<OrderStatus, int> ByStatus,
    double TotalWeightKg);

public interface IDeliveryOrderRepository
{
    Task<Entities.DeliveryOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Entities.DeliveryOrder?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Entities.DeliveryOrder?> GetByRefAsync(string sourceSystemKey, string orderRef, CancellationToken cancellationToken = default);
    Task<List<Entities.DeliveryOrder>> GetByStatusAsync(OrderStatus status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<List<Entities.DeliveryOrder>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> CountAsync(OrderStatus? status, CancellationToken cancellationToken = default);
    Task<(List<Entities.DeliveryOrder> Items, int TotalCount)> SearchAsync(
        DeliveryOrderSearchFilters filters,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<DeliveryOrderStats> GetStatsAsync(CancellationToken cancellationToken = default);
    Task<List<Entities.DeliveryOrder>> GetOrdersByItemIdsAsync(IEnumerable<string> itemIds, CancellationToken cancellationToken = default);
    Task<List<Entities.DeliveryOrder>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<(List<Item> Items, int TotalCount)> SearchItemsAsync(string? itemId, ItemStatus? status, string? pickupCode, Guid? pickupStationId, string? dropCode, Guid? dropStationId, int page, int pageSize, CancellationToken cancellationToken = default);
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
    // Dedup helper: has an audit event of this type, whose Details contain the
    // given marker, already been recorded for the order? Makes OMS callbacks
    // idempotent per shipment when RIOT3 (SUB_TASK_FINISHED) or a self-managed
    // source re-emits drop-completed several times for the same trip.
    Task<bool> ExistsAsync(Guid orderId, string eventType, string detailsContains, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
