using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace DTMS.DeliveryOrder.Infrastructure.Projections;

/// <summary>
/// Concrete implementation of <see cref="IOrderStatusHistoryProjectionStore"/>
/// backed by <see cref="DeliveryOrderDbContext"/>. <see cref="AppendAsync"/>
/// performs the inbox+history insert in one SaveChanges call so partial
/// failure can't leave the inbox marker without the history row or vice
/// versa.
/// </summary>
public class OrderStatusHistoryProjectionStore : IOrderStatusHistoryProjectionStore
{
    private readonly DeliveryOrderDbContext _db;

    public OrderStatusHistoryProjectionStore(DeliveryOrderDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, cancellationToken);

    public async Task<(string ToStatus, DateTime OccurredAt)?> GetLatestForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var row = await _db.OrderStatusHistory
            .AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderByDescending(r => r.OccurredAt)
            .Select(r => new { r.ToStatus, r.OccurredAt })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : (row.ToStatus, row.OccurredAt);
    }

    public async Task AppendAsync(
        string projectorName, Guid eventId, Guid orderId,
        string? fromStatus, string toStatus, DateTime occurredAt, string? reason,
        CancellationToken cancellationToken = default)
    {
        _db.OrderStatusHistory.Add(
            new OrderStatusHistoryRow(eventId, orderId, fromStatus, toStatus, occurredAt, reason));
        _db.ProjectionInbox.Add(
            new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
