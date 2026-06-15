using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using AMR.DeliveryPlanning.Dispatch.Domain.Services;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Services;

// Phase P5.3 — Implements the Dispatch-side bridge by querying the
// DeliveryOrder read side. Lives in DeliveryOrder.Infrastructure (data
// access) so Dispatch.* projects don't take a hard dependency on
// DeliveryOrderDbContext. Wired up in ModuleServiceRegistration's
// DeliveryOrder section.
public sealed class DeliveryOrderTripItemSnapshotProvider : ITripItemSnapshotProvider
{
    private readonly DeliveryOrderDbContext _context;

    public DeliveryOrderTripItemSnapshotProvider(DeliveryOrderDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TripItemSnapshot>> GetForTripAsync(
        Guid tripId, CancellationToken cancellationToken)
    {
        // One round-trip per call — projector reads denormalized columns
        // we already have (Items.TripId + the owning order's OrderRef/Status).
        // No Include + no tracking: this is a read-only enrichment.
        var rows = await (
            from item in _context.Items.AsNoTracking()
            join order in _context.DeliveryOrders.AsNoTracking() on item.DeliveryOrderId equals order.Id
            where item.TripId == tripId
            orderby item.ItemSeq
            select new
            {
                ItemPk = item.Id,
                item.ItemSeq,
                LotNo = item.ItemId,
                ItemStatus = item.Status,
                PickupCode = item.PickupLocationCode,
                DropCode = item.DropLocationCode,
                item.WeightKg,
                DeliveryOrderId = order.Id,
                order.OrderRef,
                OrderStatus = order.Status
            }
        ).ToListAsync(cancellationToken);

        return rows
            .Select(r => new TripItemSnapshot(
                ItemPk: r.ItemPk,
                ItemSeq: r.ItemSeq,
                LotNo: r.LotNo,
                ItemStatus: r.ItemStatus.ToString(),
                PickupCode: r.PickupCode,
                DropCode: r.DropCode,
                WeightKg: r.WeightKg,
                DeliveryOrderId: r.DeliveryOrderId,
                OrderRef: r.OrderRef,
                OrderStatus: r.OrderStatus.ToString()))
            .ToList();
    }
}
