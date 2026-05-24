using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Repositories;

public class DeliveryOrderRepository : IDeliveryOrderRepository
{
    private readonly DeliveryOrderDbContext _context;
    public DeliveryOrderRepository(DeliveryOrderDbContext context) => _context = context;

    public Task<Domain.Entities.DeliveryOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.DeliveryOrders
            .Include(o => o.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Domain.Entities.DeliveryOrder?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.DeliveryOrders
            .AsNoTracking()
            .Include(o => o.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Domain.Entities.DeliveryOrder?> GetByRefAsync(SourceSystem sourceSystem, string orderRef, CancellationToken cancellationToken = default)
        => _context.DeliveryOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.SourceSystem == sourceSystem && o.OrderRef == orderRef, cancellationToken);

    public async Task<List<Domain.Entities.DeliveryOrder>> GetByStatusAsync(OrderStatus status, int page, int pageSize, CancellationToken cancellationToken = default)
        => await _context.DeliveryOrders
            .AsNoTracking()
            .Where(o => o.Status == status)
            .OrderByDescending(o => o.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task<List<Domain.Entities.DeliveryOrder>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        => await _context.DeliveryOrders
            .AsNoTracking()
            .OrderByDescending(o => o.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(OrderStatus? status, CancellationToken cancellationToken = default)
        => status.HasValue
            ? _context.DeliveryOrders.CountAsync(o => o.Status == status.Value, cancellationToken)
            : _context.DeliveryOrders.CountAsync(cancellationToken);

    public async Task<List<Domain.Entities.DeliveryOrder>> GetOrdersByItemSkusAsync(
        IEnumerable<string> skus, CancellationToken cancellationToken = default)
    {
        var skuList = skus.ToList();
        return await _context.DeliveryOrders
            .Include(o => o.Items)
            .Where(o => o.Items.Any(p => skuList.Contains(p.Sku)))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Domain.Entities.DeliveryOrder>> GetByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        return await _context.DeliveryOrders
            .AsNoTracking()
            .Where(o => idList.Contains(o.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<Domain.Entities.Item> Items, int TotalCount)> SearchItemsAsync(
        string? sku, Domain.Enums.CargoType? cargoType, Domain.Enums.ItemStatus? status,
        string? pickupCode, Guid? pickupStationId,
        string? dropCode, Guid? dropStationId,
        string? partNo, string? wo, string? line, string? vendor, string? dateCode, string? tradingCode,
        string? inventoryNo, string? po, string? traceId, string? lotNo,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Items.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(sku))
            query = query.Where(i => i.Sku.Contains(sku));
        if (cargoType.HasValue)
            query = query.Where(i => i.CargoType == cargoType.Value);
        if (status.HasValue)
            query = query.Where(i => i.Status == status.Value);
        if (!string.IsNullOrEmpty(pickupCode))
            query = query.Where(i => i.PickupLocation.Code == pickupCode);
        if (pickupStationId.HasValue)
            query = query.Where(i => i.PickupLocation.StationId == pickupStationId.Value);
        if (!string.IsNullOrEmpty(dropCode))
            query = query.Where(i => i.DropLocation.Code == dropCode);
        if (dropStationId.HasValue)
            query = query.Where(i => i.DropLocation.StationId == dropStationId.Value);
        if (!string.IsNullOrEmpty(partNo))
            query = query.Where(i => i.CargoSpecific != null && i.CargoSpecific.PartNo != null && i.CargoSpecific.PartNo.Contains(partNo));
        if (!string.IsNullOrEmpty(wo))
            query = query.Where(i => i.CargoSpecific != null && i.CargoSpecific.Wo == wo);
        if (!string.IsNullOrEmpty(line))
            query = query.Where(i => i.CargoSpecific != null && i.CargoSpecific.Line == line);
        if (!string.IsNullOrEmpty(vendor))
            query = query.Where(i => i.CargoSpecific != null && i.CargoSpecific.Vendor != null && i.CargoSpecific.Vendor.Contains(vendor));
        if (!string.IsNullOrEmpty(dateCode))
            query = query.Where(i => i.CargoSpecific != null && i.CargoSpecific.DateCode == dateCode);
        if (!string.IsNullOrEmpty(tradingCode))
            query = query.Where(i => i.CargoSpecific != null && i.CargoSpecific.TradingCode == tradingCode);
        if (!string.IsNullOrEmpty(inventoryNo))
            query = query.Where(i => i.CargoSpecific != null && i.CargoSpecific.InventoryNo != null && i.CargoSpecific.InventoryNo.Contains(inventoryNo));
        if (!string.IsNullOrEmpty(po))
            query = query.Where(i => i.CargoSpecific != null && i.CargoSpecific.Po == po);
        if (!string.IsNullOrEmpty(traceId))
            query = query.Where(i => i.CargoSpecific != null && i.CargoSpecific.TraceId == traceId);
        if (!string.IsNullOrEmpty(lotNo))
            query = query.Where(i => i.CargoSpecific != null && i.CargoSpecific.LotNo != null && i.CargoSpecific.LotNo.Contains(lotNo));

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(i => i.DeliveryOrderId)
            .ThenBy(i => i.ItemSeq)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Domain.Entities.Item?> GetItemByIdAsync(Guid itemId, CancellationToken cancellationToken = default)
        => _context.Items
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

    public async Task AddAsync(Domain.Entities.DeliveryOrder order, CancellationToken cancellationToken = default)
    {
        await _context.DeliveryOrders.AddAsync(order, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<Domain.Entities.DeliveryOrder> orders, CancellationToken cancellationToken = default)
        => await _context.DeliveryOrders.AddRangeAsync(orders, cancellationToken);

    public Task RemoveItemsAsync(IEnumerable<Domain.Entities.Item> items, CancellationToken cancellationToken = default)
    {
        _context.Items.RemoveRange(items);
        return Task.CompletedTask;
    }

    public Task AddItemsAsync(IEnumerable<Domain.Entities.Item> items, CancellationToken cancellationToken = default)
    {
        _context.Items.AddRange(items);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
