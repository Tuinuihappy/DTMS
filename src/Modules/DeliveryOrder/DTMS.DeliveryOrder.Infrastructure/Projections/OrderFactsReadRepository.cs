using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.DeliveryOrder.Infrastructure.Projections;

public class OrderFactsReadRepository : IOrderFactsReadRepository
{
    private readonly DeliveryOrderDbContext _db;

    public OrderFactsReadRepository(DeliveryOrderDbContext db) => _db = db;

    public async Task<IReadOnlyList<OrderFactsEntry>> QueryAsync(
        OrderFactsFilters f, CancellationToken ct)
    {
        var q = BuildQuery(f);
        return await q
            .OrderByDescending(r => r.CreatedAt)
            .Take(f.Limit)
            .Select(r => Map(r))
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(OrderFactsFilters f, CancellationToken ct)
        => BuildQuery(f).CountAsync(ct);

    private IQueryable<OrderFactsRow> BuildQuery(OrderFactsFilters f)
    {
        var q = _db.OrderFacts.AsNoTracking().AsQueryable();
        if (f.FromCreatedAtUtc is not null) q = q.Where(r => r.CreatedAt >= f.FromCreatedAtUtc);
        if (f.ToCreatedAtUtc is not null)   q = q.Where(r => r.CreatedAt <  f.ToCreatedAtUtc);
        if (!string.IsNullOrEmpty(f.Priority))     q = q.Where(r => r.Priority == f.Priority);
        if (!string.IsNullOrEmpty(f.FinalStatus))  q = q.Where(r => r.FinalStatus == f.FinalStatus);
        if (!string.IsNullOrEmpty(f.SourceSystem)) q = q.Where(r => r.SourceSystem == f.SourceSystem);
        return q;
    }

    private static OrderFactsEntry Map(OrderFactsRow r) => new(
        r.OrderId, r.OrderRef, r.SourceSystem, r.Priority, r.TransportMode, r.RequestedBy,
        r.FinalStatus, r.FailureReason,
        r.TotalItems, r.TotalQuantity, r.TotalWeightKg,
        r.CreatedAt, r.SubmittedAt, r.ConfirmedAt, r.DispatchedAt, r.InProgressAt,
        r.CompletedAt, r.PartiallyCompletedAt, r.FailedAt, r.CancelledAt, r.RejectedAt,
        r.HeldAt, r.ReleasedAt,
        r.TimeToConfirmSec, r.TimeToDispatchSec, r.TimeToCompleteSec,
        r.SlaConfirmBreached, r.SlaCompleteBreached,
        r.UpdatedAt);
}
