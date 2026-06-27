using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace DTMS.DeliveryOrder.Infrastructure.Projections;

public class OrderFunnelProjectionStore : IOrderFunnelProjectionStore
{
    private readonly DeliveryOrderDbContext _db;

    public OrderFunnelProjectionStore(DeliveryOrderDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, cancellationToken);

    public async Task IncrementAsync(
        string projectorName, Guid eventId, DateTime occurredOn, string status,
        CancellationToken cancellationToken = default)
    {
        // Align bucket to start-of-hour UTC. Truncating to hour precision
        // collapses sub-hour duplicates into the same row so the projection
        // stays compact even under burst traffic.
        var bucketHour = new DateTime(
            occurredOn.Year, occurredOn.Month, occurredOn.Day,
            occurredOn.Hour, 0, 0, DateTimeKind.Utc);

        var row = await _db.OrderFunnelHourly
            .FirstOrDefaultAsync(r => r.BucketHour == bucketHour, cancellationToken);

        if (row is null)
        {
            row = new OrderFunnelHourlyRow(bucketHour);
            _db.OrderFunnelHourly.Add(row);
        }

        row.IncrementStatus(status);

        _db.ProjectionInbox.Add(
            new InboxMessage(projectorName, eventId, DateTime.UtcNow));

        await _db.SaveChangesAsync(cancellationToken);
    }
}
