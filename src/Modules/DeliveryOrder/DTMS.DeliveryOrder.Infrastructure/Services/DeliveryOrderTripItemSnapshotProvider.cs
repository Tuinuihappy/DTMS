using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.Dispatch.Domain.Services;
using DTMS.Dispatch.IntegrationEvents;
using Microsoft.EntityFrameworkCore;

namespace DTMS.DeliveryOrder.Infrastructure.Services;

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
        // we already have (Items.TripId + the owning order's OrderRef and
        // transport mode). No Include + no tracking: read-only enrichment.
        // The order join stays for OrderRef/RequestedTransportMode; the
        // order's Status is deliberately NOT snapshotted (see TripItemSnapshot).
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
                item.Description,
                QuantityValue = item.Quantity.Value,
                QuantityUom = item.Quantity.Uom,
                DeliveryOrderId = order.Id,
                order.OrderRef,
                OrderTransportMode = order.RequestedTransportMode
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
                Description: r.Description,
                QuantityValue: r.QuantityValue,
                QuantityUom: r.QuantityUom,
                OrderTransportMode: r.OrderTransportMode?.ToString()))
            .ToList();
    }
}
