using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Projections;

/// <summary>
/// EF-backed write side of bi.OrderFacts. Mutations are tracked on the
/// DbContext and persisted together with the inbox row in
/// <see cref="MarkProcessedAsync"/>'s SaveChanges — one transaction
/// per consumed event, atomic with idempotency bookkeeping.
/// </summary>
public class OrderFactsProjectionStore : IOrderFactsProjectionStore
{
    private readonly DeliveryOrderDbContext _db;

    public OrderFactsProjectionStore(DeliveryOrderDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken ct)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, ct);

    public async Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken ct)
    {
        _db.ProjectionInbox.Add(new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpsertOnConfirmAsync(
        Guid orderId, DateTime confirmedAt, string priority, string? transportMode,
        int totalItems, double totalWeightKg, CancellationToken ct)
    {
        var row = await _db.OrderFacts.FirstOrDefaultAsync(r => r.OrderId == orderId, ct);
        if (row is null)
        {
            // Confirmed is the row's birth event. Dimensional fields the
            // event doesn't carry (OrderRef, RequestedBy, SourceSystem)
            // stay "(unknown)" until backfill SQL fills them or a later
            // refresh event arrives. MVP — Phase P5.5 can enrich the
            // event payload if reports need those fields live.
            row = OrderFactsRow.Create(
                orderId,
                createdAt: confirmedAt,
                orderRef: "(unknown)",
                sourceSystem: "(unknown)",
                priority: priority,
                transportMode: transportMode,
                requestedBy: null,
                totalItems: totalItems,
                totalQuantity: 0,
                totalWeightKg: totalWeightKg,
                finalStatus: "Confirmed");
            row.SetConfirmedAt(confirmedAt, priority, transportMode, totalItems, totalWeightKg);
            _db.OrderFacts.Add(row);
        }
        else
        {
            row.SetConfirmedAt(confirmedAt, priority, transportMode, totalItems, totalWeightKg);
        }
    }

    public async Task SetSubmittedAtAsync(Guid orderId, DateTime at, CancellationToken ct)
        => (await Find(orderId, ct))?.SetSubmittedAt(at);

    public async Task SetDispatchedAtAsync(Guid orderId, DateTime at, CancellationToken ct)
        => (await Find(orderId, ct))?.SetDispatchedAt(at);

    public async Task SetInProgressAtAsync(Guid orderId, DateTime at, CancellationToken ct)
        => (await Find(orderId, ct))?.SetInProgressAt(at);

    public async Task SetCompletedAtAsync(Guid orderId, DateTime at, CancellationToken ct)
        => (await Find(orderId, ct))?.SetCompletedAt(at);

    public async Task SetPartiallyCompletedAtAsync(Guid orderId, DateTime at, CancellationToken ct)
        => (await Find(orderId, ct))?.SetPartiallyCompletedAt(at);

    public async Task SetFailedAtAsync(Guid orderId, DateTime at, string? reason, CancellationToken ct)
        => (await Find(orderId, ct))?.SetFailedAt(at, reason);

    public async Task SetCancelledAtAsync(Guid orderId, DateTime at, string? reason, CancellationToken ct)
        => (await Find(orderId, ct))?.SetCancelledAt(at, reason);

    public async Task SetRejectedAtAsync(Guid orderId, DateTime at, string? reason, CancellationToken ct)
        => (await Find(orderId, ct))?.SetRejectedAt(at, reason);

    public async Task SetHeldAtAsync(Guid orderId, DateTime at, string? reason, CancellationToken ct)
        => (await Find(orderId, ct))?.SetHeldAt(at, reason);

    public async Task SetReleasedAtAsync(Guid orderId, DateTime at, CancellationToken ct)
        => (await Find(orderId, ct))?.SetReleasedAt(at);

    private Task<OrderFactsRow?> Find(Guid orderId, CancellationToken ct)
        => _db.OrderFacts.FirstOrDefaultAsync(r => r.OrderId == orderId, ct);
}
