using DTMS.Planning.Application.Projections;
using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Planning.Infrastructure.Projections;

public class JobFactsReadRepository : IJobFactsReadRepository
{
    private readonly PlanningDbContext _db;

    public JobFactsReadRepository(PlanningDbContext db) => _db = db;

    public async Task<IReadOnlyList<JobFactsEntry>> QueryAsync(
        JobFactsFilters f, CancellationToken ct)
    {
        var q = BuildQuery(f);
        return await q
            .OrderByDescending(r => r.CreatedAt)
            .Take(f.Limit)
            .Select(r => new JobFactsEntry(
                r.JobId, r.DeliveryOrderId, r.AssignedVehicleId, r.LatestTripId,
                r.VendorOrderKey, r.FinalStatus, r.FailureReason, r.FailureCategory,
                r.AttemptNumber,
                r.CreatedAt, r.AssignedAt, r.CommittedAt, r.DispatchedAt, r.ExecutingAt,
                r.CompletedAt, r.FailedAt, r.CancelledAt,
                r.TimeToDispatchSec, r.TimeToCompleteSec, r.SlaDispatchBreached,
                r.UpdatedAt))
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(JobFactsFilters f, CancellationToken ct)
        => BuildQuery(f).CountAsync(ct);

    private IQueryable<JobFactsRow> BuildQuery(JobFactsFilters f)
    {
        var q = _db.JobFacts.AsNoTracking().AsQueryable();
        if (f.FromCreatedAtUtc is not null) q = q.Where(r => r.CreatedAt >= f.FromCreatedAtUtc);
        if (f.ToCreatedAtUtc is not null)   q = q.Where(r => r.CreatedAt <  f.ToCreatedAtUtc);
        if (!string.IsNullOrEmpty(f.FinalStatus))      q = q.Where(r => r.FinalStatus == f.FinalStatus);
        if (f.MinAttemptNumber is not null)            q = q.Where(r => r.AttemptNumber >= f.MinAttemptNumber);
        if (!string.IsNullOrEmpty(f.FailureCategory))  q = q.Where(r => r.FailureCategory == f.FailureCategory);
        return q;
    }
}
