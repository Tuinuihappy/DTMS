using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.DeliveryOrder.Infrastructure.Projections;

public class OrderActivityReadRepository : IOrderActivityReadRepository
{
    private readonly DeliveryOrderDbContext _db;

    public OrderActivityReadRepository(DeliveryOrderDbContext db) => _db = db;

    public async Task<IReadOnlyList<OrderActivityEntry>> GetForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.OrderActivity
            .AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderByDescending(r => r.OccurredAt)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new OrderActivityEntry(
            r.Id, r.EventId, r.OrderId, r.Category, r.EventType,
            r.Details, r.ActorId, r.OccurredAt,
            r.RelatedTripId, r.AttemptNumber,
            r.Channel, r.DisplayName)).ToList();
    }
}
