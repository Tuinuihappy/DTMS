using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.DeliveryOrder.Infrastructure.Repositories;

public class DeliveryOrderRepository : IDeliveryOrderRepository
{
    private readonly DeliveryOrderDbContext _context;
    public DeliveryOrderRepository(DeliveryOrderDbContext context) => _context = context;

    public Task<Domain.Entities.DeliveryOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.DeliveryOrders
            .Include(o => o.Items)
                .ThenInclude(i => i.PodEvents)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Domain.Entities.DeliveryOrder?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.DeliveryOrders
            .AsNoTracking()
            .Include(o => o.Items)
                .ThenInclude(i => i.PodEvents)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Domain.Entities.DeliveryOrder?> GetByRefAsync(string sourceSystemKey, string orderRef, CancellationToken cancellationToken = default)
        => _context.DeliveryOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.SourceSystemKey == sourceSystemKey && o.OrderRef == orderRef, cancellationToken);

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

    public async Task<(List<Domain.Entities.DeliveryOrder> Items, int TotalCount)> SearchAsync(
        DeliveryOrderSearchFilters filters,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DeliveryOrders.AsNoTracking().AsQueryable();

        if (filters.Status.HasValue)
            query = query.Where(o => o.Status == filters.Status.Value);
        // Bucket — a group of statuses (Active/Completed/Terminal). Applied
        // as a separate predicate so callers can combine a bucket with an
        // exact status (rare, but harmless) or use them independently.
        if (filters.Bucket.HasValue)
        {
            var bucketStatuses = OrderStatusBuckets.For(filters.Bucket.Value);
            query = query.Where(o => bucketStatuses.Contains(o.Status));
        }
        if (filters.Priority.HasValue)
            query = query.Where(o => o.Priority == filters.Priority.Value);
        if (filters.TransportMode.HasValue)
            query = query.Where(o => o.RequestedTransportMode == filters.TransportMode.Value);

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            // Case-insensitive substring across the four free-text fields
            // users actually scan for. Npgsql translates EF.Functions.ILike
            // into PostgreSQL's ILIKE; the wildcards run on the DB side so
            // we never materialize the table.
            var pattern = $"%{filters.Search.Trim()}%";
            query = query.Where(o =>
                EF.Functions.ILike(o.OrderRef, pattern) ||
                (o.RequestedBy != null && EF.Functions.ILike(o.RequestedBy, pattern)) ||
                (o.CreatedBy != null && EF.Functions.ILike(o.CreatedBy, pattern)) ||
                (o.Notes != null && EF.Functions.ILike(o.Notes, pattern)));
        }

        // Whitelist sort columns to prevent injection-by-sort. CreatedDate
        // is the default and the column most ops users expect.
        query = (filters.SortBy?.ToLowerInvariant(), filters.SortDescending) switch
        {
            ("orderref", false) => query.OrderBy(o => o.OrderRef),
            ("orderref", true) => query.OrderByDescending(o => o.OrderRef),
            ("priority", false) => query.OrderBy(o => o.Priority),
            ("priority", true) => query.OrderByDescending(o => o.Priority),
            ("status", false) => query.OrderBy(o => o.Status),
            ("status", true) => query.OrderByDescending(o => o.Status),
            ("totalweightkg", false) => query.OrderBy(o => o.TotalWeightKg),
            ("totalweightkg", true) => query.OrderByDescending(o => o.TotalWeightKg),
            (_, false) => query.OrderBy(o => o.CreatedDate),
            _ => query.OrderByDescending(o => o.CreatedDate),
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<DeliveryOrderStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        // Two DB roundtrips (group-by + sum) instead of one fat aggregate —
        // EF Core can't combine a GroupBy with a non-grouped Sum in a single
        // SQL projection cleanly, and the two queries each hit the same
        // small table so the overhead is negligible.
        var byStatusRaw = await _context.DeliveryOrders
            .AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byStatus = byStatusRaw.ToDictionary(x => x.Status, x => x.Count);
        var total = byStatus.Values.Sum();

        var totalWeight = total == 0
            ? 0d
            : await _context.DeliveryOrders.AsNoTracking().SumAsync(o => o.TotalWeightKg, cancellationToken);

        return new DeliveryOrderStats(total, byStatus, totalWeight);
    }

    public async Task<List<Domain.Entities.DeliveryOrder>> GetOrdersByItemIdsAsync(
        IEnumerable<string> itemIds, CancellationToken cancellationToken = default)
    {
        var idList = itemIds.ToList();
        return await _context.DeliveryOrders
            .Include(o => o.Items)
            .Where(o => o.Items.Any(p => idList.Contains(p.ItemId)))
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
        string? itemId, Domain.Enums.ItemStatus? status,
        string? pickupCode, Guid? pickupStationId,
        string? dropCode, Guid? dropStationId,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Items.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(itemId))
            query = query.Where(i => i.ItemId.Contains(itemId));
        if (status.HasValue)
            query = query.Where(i => i.Status == status.Value);
        if (!string.IsNullOrEmpty(pickupCode))
            query = query.Where(i => i.PickupLocationCode == pickupCode);
        if (pickupStationId.HasValue)
            query = query.Where(i => i.PickupStationId == pickupStationId.Value);
        if (!string.IsNullOrEmpty(dropCode))
            query = query.Where(i => i.DropLocationCode == dropCode);
        if (dropStationId.HasValue)
            query = query.Where(i => i.DropStationId == dropStationId.Value);

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

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Children added through a parent's navigation while the parent
        // is in Unchanged state are inferred as Modified by EF — which
        // triggers an UPDATE WHERE Id=… that affects 0 rows and surfaces
        // as DbUpdateConcurrencyException. Reclassify any such PodEvents
        // as Added before saving. Same pattern as Dispatch's
        // TripRepository.AddNewExecutionEventsAsync.
        foreach (var entry in _context.ChangeTracker.Entries<Domain.Entities.ItemPodEvent>().ToList())
        {
            if (entry.State == EntityState.Modified)
                entry.State = EntityState.Added;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
