using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Projections;

public class TripStatusHistoryReadRepository : ITripStatusHistoryReadRepository
{
    private readonly DispatchDbContext _db;

    public TripStatusHistoryReadRepository(DispatchDbContext db) => _db = db;

    public async Task<IReadOnlyList<TripStatusHistoryEntry>> GetForTripAsync(
        Guid tripId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.TripStatusHistory
            .AsNoTracking()
            .Where(r => r.TripId == tripId)
            .OrderByDescending(r => r.OccurredAt)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<TripStatusHistoryEntry>> GetForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.TripStatusHistory
            .AsNoTracking()
            .Where(r => r.DeliveryOrderId == orderId)
            .OrderByDescending(r => r.OccurredAt)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    private static TripStatusHistoryEntry Map(TripStatusHistoryRow r) =>
        new(r.EventId, r.TripId, r.DeliveryOrderId, r.JobId,
            r.FromStatus, r.ToStatus, r.OccurredAt, r.Reason);
}
