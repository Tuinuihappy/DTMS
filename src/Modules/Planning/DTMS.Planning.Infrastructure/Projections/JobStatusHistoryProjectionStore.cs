using AMR.DeliveryPlanning.Planning.Application.Projections;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Projections;

public class JobStatusHistoryProjectionStore : IJobStatusHistoryProjectionStore
{
    private readonly PlanningDbContext _db;

    public JobStatusHistoryProjectionStore(PlanningDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, cancellationToken);

    public async Task<(string ToStatus, DateTime OccurredAt)?> GetLatestForJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var row = await _db.JobStatusHistory
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.OccurredAt)
            .Select(r => new { r.ToStatus, r.OccurredAt })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : (row.ToStatus, row.OccurredAt);
    }

    public async Task AppendAsync(
        string projectorName, Guid eventId, Guid jobId, Guid deliveryOrderId,
        string? fromStatus, string toStatus, DateTime occurredAt, string? reason,
        CancellationToken cancellationToken = default)
    {
        _db.JobStatusHistory.Add(
            new JobStatusHistoryRow(eventId, jobId, deliveryOrderId, fromStatus, toStatus, occurredAt, reason));
        _db.ProjectionInbox.Add(
            new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
