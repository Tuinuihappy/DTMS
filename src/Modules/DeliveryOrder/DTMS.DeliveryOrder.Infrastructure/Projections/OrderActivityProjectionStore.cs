using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace DTMS.DeliveryOrder.Infrastructure.Projections;

public class OrderActivityProjectionStore : IOrderActivityProjectionStore
{
    private readonly DeliveryOrderDbContext _db;

    public OrderActivityProjectionStore(DeliveryOrderDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, cancellationToken);

    public async Task AppendAsync(
        string projectorName, Guid eventId, Guid orderId,
        string category, string eventType, string? details, string? actorId,
        DateTime occurredAt, Guid? relatedTripId, int? attemptNumber,
        CancellationToken cancellationToken = default,
        string? channel = null, string? displayName = null,
        string? systemKey = null)
    {
        _db.OrderActivity.Add(new OrderActivityRow(
            eventId, orderId, category, eventType,
            details, actorId, occurredAt, relatedTripId, attemptNumber,
            channel, displayName, systemKey));
        _db.ProjectionInbox.Add(
            new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
