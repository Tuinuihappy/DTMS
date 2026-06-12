using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Projections;

public class TripStatusHistoryProjectionStore : ITripStatusHistoryProjectionStore
{
    private readonly DispatchDbContext _db;

    public TripStatusHistoryProjectionStore(DispatchDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, cancellationToken);

    public async Task<TripHistoryLatest?> GetLatestForTripAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        var row = await _db.TripStatusHistory
            .AsNoTracking()
            .Where(r => r.TripId == tripId)
            .OrderByDescending(r => r.OccurredAt)
            .Select(r => new { r.ToStatus, r.OccurredAt, r.DeliveryOrderId, r.JobId })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new TripHistoryLatest(row.ToStatus, row.OccurredAt, row.DeliveryOrderId, row.JobId);
    }

    public async Task AppendAsync(
        string projectorName, Guid eventId, Guid tripId,
        Guid? deliveryOrderId, Guid? jobId,
        string? fromStatus, string toStatus, DateTime occurredAt, string? reason,
        CancellationToken cancellationToken = default)
    {
        _db.TripStatusHistory.Add(
            new TripStatusHistoryRow(eventId, tripId, deliveryOrderId, jobId, fromStatus, toStatus, occurredAt, reason));
        _db.ProjectionInbox.Add(
            new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
