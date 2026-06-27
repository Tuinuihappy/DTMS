using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Dispatch.Infrastructure.Projections;

public class TripItemsReadRepository : ITripItemsReadRepository
{
    private readonly DispatchDbContext _db;

    public TripItemsReadRepository(DispatchDbContext db) => _db = db;

    public async Task<IReadOnlyList<TripItemReadModel>> GetByTripAsync(
        Guid tripId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.TripItems
            .AsNoTracking()
            .Where(r => r.TripId == tripId)
            .OrderBy(r => r.ItemSeq)
            .Select(r => new TripItemReadModel(
                r.TripId,
                r.ItemPk,
                r.DeliveryOrderId,
                r.OrderRef,
                r.OrderStatus,
                r.OrderTransportMode,
                r.LotNo,
                r.ItemSeq,
                r.ItemStatus,
                r.PickupCode,
                r.DropCode,
                r.WeightKg,
                r.Description,
                r.QuantityValue,
                r.QuantityUom,
                r.BoundAt,
                r.LastEventAt))
            .ToListAsync(cancellationToken);

        return rows;
    }
}
