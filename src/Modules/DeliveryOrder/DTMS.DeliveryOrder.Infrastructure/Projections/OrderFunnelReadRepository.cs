using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Projections;

public class OrderFunnelReadRepository : IOrderFunnelReadRepository
{
    private readonly DeliveryOrderDbContext _db;

    public OrderFunnelReadRepository(DeliveryOrderDbContext db) => _db = db;

    public async Task<IReadOnlyList<OrderFunnelBucketEntry>> GetRangeAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _db.OrderFunnelHourly
            .AsNoTracking()
            .Where(r => r.BucketHour >= fromUtc && r.BucketHour < toUtc)
            .OrderBy(r => r.BucketHour)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new OrderFunnelBucketEntry(
            r.BucketHour,
            r.Confirmed, r.Dispatched, r.InProgress,
            r.Completed, r.PartiallyCompleted,
            r.Failed, r.Cancelled, r.Rejected,
            r.Held, r.Released)).ToList();
    }
}
