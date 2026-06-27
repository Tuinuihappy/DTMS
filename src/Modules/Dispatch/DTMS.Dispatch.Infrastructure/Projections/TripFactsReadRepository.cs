using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Dispatch.Infrastructure.Projections;

public class TripFactsReadRepository : ITripFactsReadRepository
{
    private readonly DispatchDbContext _db;

    public TripFactsReadRepository(DispatchDbContext db) => _db = db;

    public async Task<IReadOnlyList<TripFactsEntry>> QueryAsync(
        TripFactsFilters f, CancellationToken ct)
    {
        var q = BuildQuery(f);
        return await q
            .OrderByDescending(r => r.CreatedAt)
            .Take(f.Limit)
            .Select(r => new TripFactsEntry(
                r.TripId, r.DeliveryOrderId, r.JobId, r.VehicleId,
                r.VendorUpperKey, r.VendorVehicleKey,
                r.FinalStatus, r.FailureReason, r.PauseCount,
                r.CreatedAt, r.StartedAt, r.FirstPausedAt, r.LastResumedAt,
                r.CompletedAt, r.FailedAt, r.CancelledAt,
                r.TimeToStartSec, r.TimeToCompleteSec, r.SlaCompleteBreached,
                r.UpdatedAt))
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(TripFactsFilters f, CancellationToken ct)
        => BuildQuery(f).CountAsync(ct);

    private IQueryable<TripFactsRow> BuildQuery(TripFactsFilters f)
    {
        var q = _db.TripFacts.AsNoTracking().AsQueryable();
        if (f.FromCreatedAtUtc is not null) q = q.Where(r => r.CreatedAt >= f.FromCreatedAtUtc);
        if (f.ToCreatedAtUtc is not null)   q = q.Where(r => r.CreatedAt <  f.ToCreatedAtUtc);
        if (!string.IsNullOrEmpty(f.VendorUpperKey))   q = q.Where(r => r.VendorUpperKey == f.VendorUpperKey);
        if (!string.IsNullOrEmpty(f.VendorVehicleKey)) q = q.Where(r => r.VendorVehicleKey == f.VendorVehicleKey);
        if (!string.IsNullOrEmpty(f.FinalStatus))      q = q.Where(r => r.FinalStatus == f.FinalStatus);
        return q;
    }
}
