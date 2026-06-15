using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Projections;

public class OrderListViewProjectionStore : IOrderListViewProjectionStore
{
    private readonly DeliveryOrderDbContext _db;

    public OrderListViewProjectionStore(DeliveryOrderDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, cancellationToken);

    public async Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
    {
        _db.ProjectionInbox.Add(new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertOnCreateAsync(
        Guid orderId, string orderRef, string status, string sourceSystem, string priority,
        string? transportMode, string? requestedBy, string? createdBy, string? notes,
        int totalItems, double totalQuantity, double totalWeightKg,
        bool? requiresDropPod, bool? requiresPickupPod,
        DateTime createdAt, DateTime? submittedAt,
        DateTime? serviceWindowEarliestUtc, DateTime? serviceWindowLatestUtc,
        string searchText,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.OrderListView.FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);
        if (row is null)
        {
            _db.OrderListView.Add(new OrderListViewRow(
                orderId, orderRef, status, sourceSystem, priority, transportMode,
                requestedBy, createdBy, notes,
                totalItems, totalQuantity, totalWeightKg,
                requiresDropPod, requiresPickupPod,
                createdAt, updatedAt: null, submittedAt,
                serviceWindowEarliestUtc, serviceWindowLatestUtc,
                searchText));
            return;
        }

        // Row already exists: a later status event raced ahead of Created
        // (rare — same SaveChanges, same outbox batch — but possible on
        // duplicate-delivery retry). Leave the existing status intact; the
        // later handler will have set it correctly. We don't reset notes /
        // searchText either — Created's payload is a snapshot at creation
        // time, not the latest truth.
    }

    public async Task UpdateStatusAsync(Guid orderId, string newStatus, DateTime occurredAt, CancellationToken cancellationToken = default)
    {
        var row = await _db.OrderListView.FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);
        if (row is null) return;   // pre-Confirm event — no row yet
        row.UpdateStatus(newStatus, occurredAt);
    }

    public async Task SetTripDerivedFieldsAsync(Guid orderId, bool hasFailedTrip, Guid? latestTripId, CancellationToken cancellationToken = default)
    {
        var row = await _db.OrderListView.FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);
        if (row is null) return;
        row.SetTripDerivedFields(hasFailedTrip, latestTripId);
    }

    public async Task SetJobDerivedFieldsAsync(Guid orderId, bool hasActiveJob, string? latestJobStatus, CancellationToken cancellationToken = default)
    {
        var row = await _db.OrderListView.FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);
        if (row is null) return;
        row.SetJobDerivedFields(hasActiveJob, latestJobStatus);
    }
}
