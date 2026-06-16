using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Projections;

public class TripItemsProjectionStore : ITripItemsProjectionStore
{
    private readonly DispatchDbContext _db;

    public TripItemsProjectionStore(DispatchDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, cancellationToken);

    public async Task InsertBindingsAsync(
        string projectorName, Guid eventId,
        Guid tripId, DateTime occurredAt,
        IReadOnlyList<TripItemSnapshot> items,
        CancellationToken cancellationToken = default)
    {
        // Pull existing (TripId, ItemPk) pairs in one round-trip so a
        // replay (or webhook + reconciler race) is idempotent without
        // relying on DB-level ON CONFLICT.
        var itemPks = items.Select(i => i.ItemPk).ToList();
        var existingPks = await _db.TripItems
            .AsNoTracking()
            .Where(r => r.TripId == tripId && itemPks.Contains(r.ItemPk))
            .Select(r => r.ItemPk)
            .ToListAsync(cancellationToken);
        var existingSet = existingPks.ToHashSet();

        foreach (var snap in items)
        {
            if (existingSet.Contains(snap.ItemPk)) continue;
            _db.TripItems.Add(new TripItemsRow(
                tripId: tripId,
                itemPk: snap.ItemPk,
                eventId: eventId,
                deliveryOrderId: snap.DeliveryOrderId,
                orderRef: snap.OrderRef,
                orderStatus: snap.OrderStatus,
                lotNo: snap.LotNo,
                itemSeq: snap.ItemSeq,
                itemStatus: snap.ItemStatus,
                pickupCode: snap.PickupCode,
                dropCode: snap.DropCode,
                weightKg: snap.WeightKg,
                description: snap.Description,
                quantityValue: snap.QuantityValue,
                quantityUom: snap.QuantityUom,
                orderTransportMode: snap.OrderTransportMode,
                boundAt: occurredAt,
                lastEventAt: occurredAt));
        }

        _db.ProjectionInbox.Add(new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordEmptyBindingAsync(
        string projectorName, Guid eventId,
        Guid tripId, DateTime occurredAt,
        CancellationToken cancellationToken = default)
    {
        _db.ProjectionInbox.Add(new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> UpdateItemStatusForTripAsync(
        string projectorName, Guid eventId,
        Guid tripId, string newItemStatus, DateTime occurredAt,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.TripItems
            .Where(r => r.TripId == tripId)
            .ToListAsync(cancellationToken);

        foreach (var r in rows)
            r.RefreshItemStatus(newItemStatus, occurredAt);

        _db.ProjectionInbox.Add(new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }
}
