using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Infrastructure.Data;
using DTMS.Facility.Application.Services;
using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Api.Adapters;

// Composition-root bridge: Facility's purge asks whether Planning, Dispatch
// or DeliveryOrder still point at a station by its Guid. Read-only, three
// small indexed queries over the candidate ids.
internal sealed class StationUsageReader : IStationUsageReader
{
    // Trips/orders in these states never dispatch again, so a station they
    // reference is history only. Failed orders stay "in use" — RetryJob can
    // re-dispatch them by their stored station ids.
    private static readonly TripStatus[] FinishedTrips =
        [TripStatus.Completed, TripStatus.Failed, TripStatus.Cancelled, TripStatus.Rejected];
    private static readonly OrderStatus[] FinishedOrders =
        [OrderStatus.Completed, OrderStatus.PartiallyCompleted, OrderStatus.Cancelled, OrderStatus.Rejected];

    private readonly PlanningDbContext _planning;
    private readonly DispatchDbContext _dispatch;
    private readonly DeliveryOrderDbContext _orders;

    public StationUsageReader(PlanningDbContext planning, DispatchDbContext dispatch, DeliveryOrderDbContext orders)
    {
        _planning = planning;
        _dispatch = dispatch;
        _orders = orders;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> FindInUseAsync(
        IReadOnlyCollection<Guid> stationIds, CancellationToken cancellationToken = default)
    {
        var ids = stationIds.Distinct().ToList();
        var inUse = new Dictionary<Guid, string>();
        if (ids.Count == 0) return inUse;

        var templates = await _planning.OrderTemplates.AsNoTracking()
            .Where(t => t.IsActive
                && ((t.PickupStationId != null && ids.Contains(t.PickupStationId.Value))
                    || (t.DropStationId != null && ids.Contains(t.DropStationId.Value))))
            .Select(t => new { t.Name, t.PickupStationId, t.DropStationId })
            .ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            var names = templates
                .Where(t => t.PickupStationId == id || t.DropStationId == id)
                .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (names.Count > 0)
                inUse[id] = names.Count == 1
                    ? $"active order template '{names[0]}' routes through it"
                    : $"{names.Count} active order templates route through it (e.g. '{names[0]}')";
        }

        var trips = await _dispatch.Trips.AsNoTracking()
            .Where(t => !FinishedTrips.Contains(t.Status)
                && ((t.PickupStationId != null && ids.Contains(t.PickupStationId.Value))
                    || (t.DropStationId != null && ids.Contains(t.DropStationId.Value))))
            .Select(t => new { t.PickupStationId, t.DropStationId })
            .ToListAsync(cancellationToken);
        foreach (var id in ids.Where(i => !inUse.ContainsKey(i)))
        {
            var count = trips.Count(t => t.PickupStationId == id || t.DropStationId == id);
            if (count > 0) inUse[id] = $"{count} unfinished trip(s) go to it";
        }

        var items = await (
            from i in _orders.Items.AsNoTracking()
            join o in _orders.DeliveryOrders.AsNoTracking() on i.DeliveryOrderId equals o.Id
            where !FinishedOrders.Contains(o.Status)
                && ((i.PickupStationId != null && ids.Contains(i.PickupStationId.Value))
                    || (i.DropStationId != null && ids.Contains(i.DropStationId.Value)))
            select new { i.PickupStationId, i.DropStationId, o.OrderRef })
            .ToListAsync(cancellationToken);
        foreach (var id in ids.Where(i => !inUse.ContainsKey(i)))
        {
            var refs = items.Where(i => i.PickupStationId == id || i.DropStationId == id)
                .Select(i => i.OrderRef).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToList();
            if (refs.Count > 0)
                inUse[id] = refs.Count == 1
                    ? $"unfinished order '{refs[0]}' uses it"
                    : $"{refs.Count} unfinished orders use it (e.g. '{refs[0]}')";
        }

        return inUse;
    }
}
