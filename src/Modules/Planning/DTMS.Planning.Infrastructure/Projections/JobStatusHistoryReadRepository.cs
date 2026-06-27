using DTMS.Planning.Application.Projections;
using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Planning.Infrastructure.Projections;

public class JobStatusHistoryReadRepository : IJobStatusHistoryReadRepository
{
    private readonly PlanningDbContext _db;

    public JobStatusHistoryReadRepository(PlanningDbContext db) => _db = db;

    public async Task<IReadOnlyList<JobStatusHistoryEntry>> GetForJobAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.JobStatusHistory
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.OccurredAt)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<JobStatusHistoryEntry>> GetForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.JobStatusHistory
            .AsNoTracking()
            .Where(r => r.DeliveryOrderId == orderId)
            .OrderByDescending(r => r.OccurredAt)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    private static JobStatusHistoryEntry Map(JobStatusHistoryRow r) =>
        new(r.EventId, r.JobId, r.DeliveryOrderId, r.FromStatus, r.ToStatus, r.OccurredAt, r.Reason);
}
