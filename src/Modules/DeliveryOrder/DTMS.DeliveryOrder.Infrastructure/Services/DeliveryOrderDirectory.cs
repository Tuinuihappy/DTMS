using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.SharedKernel.Operators;
using Microsoft.EntityFrameworkCore;

namespace DTMS.DeliveryOrder.Infrastructure.Services;

/// <summary>
/// DeliveryOrder-side implementation of <see cref="IDeliveryOrderDirectory"/>.
/// Projects only <c>RequestedBy</c> + <c>RequestedTransportMode</c> so
/// cross-module callers (Dispatch's Trips list / detail) resolve the requester
/// label and mode without materializing the full <c>DeliveryOrder</c>
/// aggregate. Mirrors
/// <c>Transport.Manual.Infrastructure.Services.OperatorDirectory</c>.
///
/// The enum → string conversion happens in memory (after materialization) so
/// it is independent of how <c>RequestedTransportMode</c> is column-mapped.
/// </summary>
public sealed class DeliveryOrderDirectory : IDeliveryOrderDirectory
{
    private readonly DeliveryOrderDbContext _db;
    public DeliveryOrderDirectory(DeliveryOrderDbContext db) => _db = db;

    public async Task<DeliveryOrderTripInfo?> GetTripInfoAsync(Guid deliveryOrderId, CancellationToken ct = default)
    {
        var row = await _db.DeliveryOrders
            .AsNoTracking()
            .Where(o => o.Id == deliveryOrderId)
            .Select(o => new { o.RequestedBy, o.RequestedTransportMode })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new DeliveryOrderTripInfo(row.RequestedBy, row.RequestedTransportMode?.ToString());
    }

    public async Task<IReadOnlyDictionary<Guid, DeliveryOrderTripInfo>> GetTripInfoAsync(
        IReadOnlyCollection<Guid> deliveryOrderIds, CancellationToken ct = default)
    {
        if (deliveryOrderIds.Count == 0)
            return new Dictionary<Guid, DeliveryOrderTripInfo>();

        // Distinct so a page with many trips for the same order sends one Id
        // per order, not one per row.
        var ids = deliveryOrderIds.Distinct().ToArray();
        var rows = await _db.DeliveryOrders
            .AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.RequestedBy, o.RequestedTransportMode })
            .ToListAsync(ct);

        return rows.ToDictionary(
            o => o.Id,
            o => new DeliveryOrderTripInfo(o.RequestedBy, o.RequestedTransportMode?.ToString()));
    }
}
