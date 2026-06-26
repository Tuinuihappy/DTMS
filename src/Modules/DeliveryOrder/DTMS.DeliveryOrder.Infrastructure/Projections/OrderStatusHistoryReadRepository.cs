using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Projections;

public class OrderStatusHistoryReadRepository : IOrderStatusHistoryReadRepository
{
    private readonly DeliveryOrderDbContext _db;

    public OrderStatusHistoryReadRepository(DeliveryOrderDbContext db) => _db = db;

    public async Task<IReadOnlyList<OrderStatusHistoryEntry>> GetForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.OrderStatusHistory
            .AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderByDescending(r => r.OccurredAt)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new OrderStatusHistoryEntry(
                r.EventId, r.OrderId, r.FromStatus, r.ToStatus, r.OccurredAt, r.Reason))
            .ToList();
    }

    public async Task<OrderStatusHistoryEntry?> GetLatestForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
    {
        var row = await _db.OrderStatusHistory
            .AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderByDescending(r => r.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new OrderStatusHistoryEntry(
                row.EventId, row.OrderId, row.FromStatus, row.ToStatus, row.OccurredAt, row.Reason);
    }
}
